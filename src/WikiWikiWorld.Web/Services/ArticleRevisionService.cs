using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Coordinates article revision write workflows.
/// </summary>
/// <param name="Context">The database context.</param>
public sealed class ArticleRevisionService(WikiWikiWorldDbContext Context)
{
	/// <summary>
	/// Updates an article by replacing its current revision with a new current revision.
	/// </summary>
	/// <param name="Command">The update command.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The new current article revision.</returns>
	/// <exception cref="ArticleRevisionWorkflowException">Thrown when the requested article cannot be updated.</exception>
	public async Task<ArticleRevision> UpdateArticleAsync(ArticleRevisionUpdateCommand Command, CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(Command);
		ArgumentException.ThrowIfNullOrWhiteSpace(Command.OriginalUrlSlug);
		ArgumentException.ThrowIfNullOrWhiteSpace(Command.NewUrlSlug);
		ArgumentException.ThrowIfNullOrWhiteSpace(Command.Title);
		ArgumentException.ThrowIfNullOrWhiteSpace(Command.Text);
		ArgumentException.ThrowIfNullOrWhiteSpace(Command.RevisionReason);

		ArticleRevision? NewRevision = null;
		IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();

		await Strategy.ExecuteAsync(async CT =>
		{
			await using IDbContextTransaction Transaction = await Context.Database.BeginImmediateTransactionAsync(CT);

			ArticleRevisionsBySlugSpec CurrentSpec = new(Command.OriginalUrlSlug, IsCurrent: true);
			ArticleRevision? CurrentArticle = await Context.ArticleRevisions
				.WithSpecification(CurrentSpec)
				.FirstOrDefaultAsync(CT);

			if (CurrentArticle is null)
			{
				throw ArticleRevisionWorkflowException.NotFound("Article not found.");
			}

			if (!Command.OriginalUrlSlug.Equals(Command.NewUrlSlug, StringComparison.OrdinalIgnoreCase))
			{
				ArticleRevisionsBySlugSpec ConflictSpec = new(Command.NewUrlSlug, IsCurrent: true);
				ArticleRevision? ExistingArticle = await Context.ArticleRevisions
					.AsNoTracking()
					.WithSpecification(ConflictSpec)
					.FirstOrDefaultAsync(CT);

				if (ExistingArticle is not null)
				{
					throw ArticleRevisionWorkflowException.Conflict("An article with this URL slug already exists.");
				}
			}

			CurrentArticle.IsCurrent = false;
			Context.ArticleRevisions.Update(CurrentArticle);

			ArticleRevision ArticleRevision = new()
			{
				CanonicalArticleId = CurrentArticle.CanonicalArticleId,
				SiteId = Command.SiteId,
				Culture = Command.Culture,
				Title = Command.Title,
				DisplayTitle = Command.DisplayTitle,
				UrlSlug = Command.NewUrlSlug,
				Type = Command.Type,
				CanonicalFileId = Command.CanonicalFileId ?? CurrentArticle.CanonicalFileId,
				Text = Command.Text,
				RevisionReason = Command.RevisionReason,
				CreatedByUserId = Command.CreatedByUserId,
				DateCreated = DateTimeOffset.UtcNow,
				IsCurrent = true
			};

			Context.ArticleRevisions.Add(ArticleRevision);

			using (WriteDurabilityScope.High())
			{
				await Context.SaveChangesAsync(CT);
			}

			await Transaction.CommitAsync(CT);

			NewRevision = ArticleRevision;
		}, CancellationToken);

		return NewRevision ?? throw new InvalidOperationException("The article update did not produce a new revision.");
	}

	/// <summary>
	/// Reverts an article to its most recent previous revision.
	/// </summary>
	/// <param name="UrlSlug">The URL slug of the article to revert.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The restored current article revision.</returns>
	/// <exception cref="ArticleRevisionWorkflowException">Thrown when the requested article cannot be reverted.</exception>
	public async Task<ArticleRevision> RevertToPreviousRevisionAsync(string UrlSlug, CancellationToken CancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(UrlSlug);

		ArticleRevision? PreviousRevision = null;
		IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();

		await Strategy.ExecuteAsync(async CT =>
		{
			await using IDbContextTransaction Transaction = await Context.Database.BeginImmediateTransactionAsync(CT);

			ArticleRevisionsBySlugSpec CurrentSpec = new(UrlSlug, IsCurrent: true);
			ArticleRevision? CurrentArticle = await Context.ArticleRevisions
				.WithSpecification(CurrentSpec)
				.FirstOrDefaultAsync(CT);

			if (CurrentArticle is null)
			{
				throw ArticleRevisionWorkflowException.NotFound("Article not found.");
			}

			PreviousRevision = await Context.ArticleRevisions
				.Where(x => x.CanonicalArticleId == CurrentArticle.CanonicalArticleId && !x.IsCurrent)
				.OrderByDescending(x => x.DateCreated)
				.FirstOrDefaultAsync(CT);

			if (PreviousRevision is null)
			{
				throw ArticleRevisionWorkflowException.NotFound("No previous revision found to revert to.");
			}

			CurrentArticle.IsCurrent = false;
			CurrentArticle.DateDeleted = DateTimeOffset.UtcNow;
			Context.ArticleRevisions.Update(CurrentArticle);

			PreviousRevision.IsCurrent = true;
			Context.ArticleRevisions.Update(PreviousRevision);

			using (WriteDurabilityScope.High())
			{
				await Context.SaveChangesAsync(CT);
			}

			await Transaction.CommitAsync(CT);
		}, CancellationToken);

		return PreviousRevision ?? throw new InvalidOperationException("The article revert did not restore a revision.");
	}

	/// <summary>
	/// Soft-deletes all revisions for an article.
	/// </summary>
	/// <param name="UrlSlug">The URL slug of the article to delete.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <exception cref="ArticleRevisionWorkflowException">Thrown when the requested article cannot be deleted.</exception>
	public async Task DeleteArticleAsync(string UrlSlug, CancellationToken CancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(UrlSlug);

		IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();

		await Strategy.ExecuteAsync(async CT =>
		{
			await using IDbContextTransaction Transaction = await Context.Database.BeginImmediateTransactionAsync(CT);

			ArticleRevisionsBySlugSpec CurrentSpec = new(UrlSlug, IsCurrent: true);
			ArticleRevision? CurrentArticle = await Context.ArticleRevisions
				.WithSpecification(CurrentSpec)
				.FirstOrDefaultAsync(CT);

			if (CurrentArticle is null)
			{
				throw ArticleRevisionWorkflowException.NotFound("Article not found.");
			}

			ArticleRevisionsByCanonicalIdSpec AllRevisionsSpec = new(CurrentArticle.CanonicalArticleId, null);
			IReadOnlyList<ArticleRevision> Revisions = await Context.ArticleRevisions
				.WithSpecification(AllRevisionsSpec)
				.ToListAsync(CT);

			DateTimeOffset DeletedUtc = DateTimeOffset.UtcNow;

			foreach (ArticleRevision Revision in Revisions)
			{
				Revision.DateDeleted = DeletedUtc;
				Revision.IsCurrent = false;
				Context.ArticleRevisions.Update(Revision);
			}

			using (WriteDurabilityScope.High())
			{
				await Context.SaveChangesAsync(CT);
			}

			await Transaction.CommitAsync(CT);
		}, CancellationToken);
	}
}

/// <summary>
/// Describes an article revision update request.
/// </summary>
/// <param name="OriginalUrlSlug">The URL slug used to locate the current revision.</param>
/// <param name="NewUrlSlug">The URL slug to store on the new revision.</param>
/// <param name="SiteId">The site identifier for the new revision.</param>
/// <param name="Culture">The culture code for the new revision.</param>
/// <param name="Title">The article title.</param>
/// <param name="DisplayTitle">The optional article display title.</param>
/// <param name="Type">The article type.</param>
/// <param name="CanonicalFileId">The optional canonical file identifier.</param>
/// <param name="Text">The article text.</param>
/// <param name="RevisionReason">The reason for the revision.</param>
/// <param name="CreatedByUserId">The user creating the revision.</param>
public sealed record ArticleRevisionUpdateCommand(
	string OriginalUrlSlug,
	string NewUrlSlug,
	int SiteId,
	string Culture,
	string Title,
	string? DisplayTitle,
	ArticleType Type,
	Guid? CanonicalFileId,
	string Text,
	string RevisionReason,
	Guid CreatedByUserId);

/// <summary>
/// Represents a known article revision workflow failure.
/// </summary>
public sealed class ArticleRevisionWorkflowException : InvalidOperationException
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ArticleRevisionWorkflowException"/> class.
	/// </summary>
	/// <param name="Kind">The workflow failure kind.</param>
	/// <param name="Message">The exception message.</param>
	private ArticleRevisionWorkflowException(ArticleRevisionWorkflowFailureKind Kind, string Message) : base(Message)
	{
		this.Kind = Kind;
	}

	/// <summary>
	/// Gets the workflow failure kind.
	/// </summary>
	public ArticleRevisionWorkflowFailureKind Kind { get; }

	/// <summary>
	/// Creates a not-found workflow exception.
	/// </summary>
	/// <param name="Message">The exception message.</param>
	/// <returns>The workflow exception.</returns>
	public static ArticleRevisionWorkflowException NotFound(string Message) => new(ArticleRevisionWorkflowFailureKind.NotFound, Message);

	/// <summary>
	/// Creates a conflict workflow exception.
	/// </summary>
	/// <param name="Message">The exception message.</param>
	/// <returns>The workflow exception.</returns>
	public static ArticleRevisionWorkflowException Conflict(string Message) => new(ArticleRevisionWorkflowFailureKind.Conflict, Message);
}

/// <summary>
/// Lists known article revision workflow failure kinds.
/// </summary>
public enum ArticleRevisionWorkflowFailureKind
{
	/// <summary>
	/// The requested article or revision was not found.
	/// </summary>
	NotFound,

	/// <summary>
	/// The requested article change conflicts with existing article state.
	/// </summary>
	Conflict
}
