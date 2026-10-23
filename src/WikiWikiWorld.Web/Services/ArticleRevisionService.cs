using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Coordinates article revision write workflows.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="Logger">The logger.</param>
public sealed class ArticleRevisionService(WikiWikiWorldDbContext Context, ILogger<ArticleRevisionService> Logger)
{
	/// <summary>Counts active discussion threads for an article within the current site's query scope.</summary>
	/// <param name="CanonicalArticleId">The stable article identifier shared by its revisions.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>The number of non-deleted threads, excluding replies.</returns>
	public Task<int> GetThreadCountAsync(Guid CanonicalArticleId, CancellationToken CancellationToken)
		=> Context.ArticleTalkSubjects.CountAsync(s => s.CanonicalArticleId == CanonicalArticleId, CancellationToken);

	/// <summary>
	/// Gets the recent authors for an article revision in most-recent contribution order.
	/// </summary>
	/// <param name="CanonicalArticleId">The canonical article identifier.</param>
	/// <param name="MaxRevisionDate">The maximum revision date to include, or <see langword="null"/> to include every revision.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The recent article authors.</returns>
	public async Task<IReadOnlyList<ArticleAuthor>> GetRecentAuthorsAsync(Guid CanonicalArticleId, DateTimeOffset? MaxRevisionDate, CancellationToken CancellationToken)
	{
		ArticleRevisionsByCanonicalIdSpec AuthorsSpec = new(CanonicalArticleId, MaxRevisionDate);

		// Let SQLite find each contributor's newest included revision so the final author list is ordered by recent activity.
		List<Guid> DistinctUserIds = await Context.ArticleRevisions
			.AsNoTracking()
			.WithSpecification(AuthorsSpec)
			.GroupBy(Revision => Revision.CreatedByUserId)
			.Select(Group => new
			{
				UserId = Group.Key,
				MaxDate = Group.Max(Revision => Revision.DateCreated)
			})
			.OrderByDescending(Row => Row.MaxDate)
			.Select(Row => Row.UserId)
			.ToListAsync(CancellationToken);

		if (DistinctUserIds.Count == 0)
		{
			return [];
		}

		UserByIdsSpec UserSpec = new(DistinctUserIds);

		// Fetch user display data in one round trip, then reapply the revision-derived order below.
		Dictionary<Guid, User> UsersById = await Context.Users
			.AsNoTracking()
			.WithSpecification(UserSpec)
			.Where(User => User.UserName != null)
			.ToDictionaryAsync(User => User.Id, CancellationToken);

		List<ArticleAuthor> Authors = new(DistinctUserIds.Count);
		foreach (Guid UserId in DistinctUserIds)
		{
			// Missing users are skipped so deleted or otherwise unavailable accounts do not break article rendering.
			if (UsersById.TryGetValue(UserId, out User? User) && User.UserName is not null)
			{
				Authors.Add(new ArticleAuthor(User.UserName, User.ProfilePicGuid));
			}
		}

		return Authors;
	}

	/// <summary>
	/// Returns the placeholder text written into a user page created automatically, so callers such as the profile
	/// can tell an untouched page from one the user has written.
	/// </summary>
	/// <param name="UserName">The user name the page belongs to.</param>
	/// <returns>The default user page Markdown.</returns>
	public static string GetDefaultUserArticleText(string? UserName) => $"Welcome to {UserName}'s page.";

	/// <summary>
	/// Gets the user's <c>@username</c> article — their home page (which also hosts their messages) — creating
	/// a placeholder revision if one does not already exist. Idempotent: an existing current revision is returned unchanged.
	/// </summary>
	/// <remarks>
	/// This is the load-bearing variant — it may throw and participates in any ambient transaction. Callers
	/// whose primary operation must not be broken by a failure should use <see cref="TryEnsureUserArticleAsync"/>.
	/// </remarks>
	/// <param name="SiteId">The site identifier for a newly created article.</param>
	/// <param name="Culture">The culture code for a newly created article.</param>
	/// <param name="User">The user whose article should exist.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The existing or newly created current article revision.</returns>
	public async Task<ArticleRevision> EnsureUserArticleAsync(int SiteId, string Culture, User User, CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(User);

		string UserSlug = $"@{User.UserName}";
		ArticleRevisionsBySlugSpec Spec = new(UserSlug, IsCurrent: true);
		ArticleRevision? ExistingArticle = await Context.ArticleRevisions
			.WithSpecification(Spec)
			.FirstOrDefaultAsync(CancellationToken);

		if (ExistingArticle is not null)
		{
			return ExistingArticle;
		}

		ArticleRevision NewUserArticle = new()
		{
			CanonicalArticleId = Guid.NewGuid(),
			SiteId = SiteId,
			Culture = Culture,
			Title = UserSlug,
			DisplayTitle = $"User: {User.UserName}",
			UrlSlug = UserSlug,
			Type = ArticleType.User,
			Text = GetDefaultUserArticleText(User.UserName),
			RevisionReason = "User page created automatically.",
			CreatedByUserId = User.Id,
			DateCreated = DateTimeOffset.UtcNow,
			IsCurrent = true
		};

		Context.ArticleRevisions.Add(NewUserArticle);
		await Context.SaveChangesAsync(CancellationToken);

		return NewUserArticle;
	}

	/// <summary>
	/// Best-effort variant of <see cref="EnsureUserArticleAsync"/> that never throws. On failure it logs a
	/// warning, detaches the failed revision and its memberships so a later save does not retry them, then
	/// re-queries once (covering the race where a concurrent request created the article).
	/// </summary>
	/// <param name="SiteId">The site identifier for a newly created article.</param>
	/// <param name="Culture">The culture code for a newly created article.</param>
	/// <param name="User">The user whose article should exist.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The existing or newly created current article revision, or <see langword="null"/> if it could not be ensured.</returns>
	public async Task<ArticleRevision?> TryEnsureUserArticleAsync(int SiteId, string Culture, User User, CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(User);

		try
		{
			return await EnsureUserArticleAsync(SiteId, Culture, User, CancellationToken);
		}
		catch (Exception Ex)
		{
			Logger.LogWarning(Ex, "Failed to ensure user article for {UserName}; continuing without it.", User.UserName);

			// A failed SaveChanges leaves the new revision in the Added state. Detach it so a later save on this
			// scoped context (e.g. sign-in resetting the access-failed counter) does not retry the bad insert.
			foreach (var Entry in Context.ChangeTracker.Entries<ArticleRevision>()
				.Where(Entry => Entry.State == EntityState.Added)
				.ToList())
			{
				foreach (ArticleCategoryMembership Membership in Entry.Entity.CategoryMemberships.ToArray())
				{
					Context.Entry(Membership).State = EntityState.Detached;
				}
				Entry.State = EntityState.Detached;
			}

			// A concurrent request may have created it in the meantime; re-read once before giving up.
			string UserSlug = $"@{User.UserName}";
			ArticleRevisionsBySlugSpec Spec = new(UserSlug, IsCurrent: true);
			return await Context.ArticleRevisions
				.WithSpecification(Spec)
				.FirstOrDefaultAsync(CancellationToken);
		}
	}

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
			await using IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync(CT);

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

			await Context.SaveChangesAsync(CT);

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
			await using IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync(CT);

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

			await Context.SaveChangesAsync(CT);

			await Transaction.CommitAsync(CT);
		}, CancellationToken);

		return PreviousRevision ?? throw new InvalidOperationException("The article revert did not restore a revision.");
	}

	/// <summary>
	/// Soft-deletes all revisions for an article, reusing tracked instances and preserving historical memberships.
	/// </summary>
	/// <param name="UrlSlug">The URL slug of the article to delete.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <exception cref="ArticleRevisionWorkflowException">Thrown when the requested article cannot be deleted.</exception>
	/// <returns>A task completing when all revision tombstones have been committed.</returns>
	public async Task DeleteArticleAsync(string UrlSlug, CancellationToken CancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(UrlSlug);

		IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();

		await Strategy.ExecuteAsync(async CT =>
		{
			await using IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync(CT);

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
				.AsTracking()
				.ToListAsync(CT);

			DateTimeOffset DeletedUtc = DateTimeOffset.UtcNow;

			foreach (ArticleRevision Revision in Revisions)
			{
				Revision.DateDeleted = DeletedUtc;
				Revision.IsCurrent = false;
				Context.ArticleRevisions.Update(Revision);
			}

			await Context.SaveChangesAsync(CT);

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
