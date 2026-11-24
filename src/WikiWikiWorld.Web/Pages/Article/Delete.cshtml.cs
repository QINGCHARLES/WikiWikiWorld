using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Web.Infrastructure;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// The confirmation page for deleting an article, reached at <c>/{slug}/delete</c> through <see cref="ArticleRouteTransformer"/>.
/// It names the article, says what deleting does, and posts the deletion. Only Senior Editors and Administrators may
/// delete articles; anyone may delete their own <c>@user</c> page.
/// </summary>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="Context">The site- and culture-filtered database context, used to find the article.</param>
/// <param name="ArticleRevisionService">Performs the soft delete and enforces the same rules.</param>
/// <param name="Permissions">Decides whether the account holds the DeleteArticles permission.</param>
[Authorize]
public sealed class DeleteModel(
	SiteResolverService SiteResolverService,
	WikiWikiWorldDbContext Context,
	ArticleRevisionService ArticleRevisionService,
	PermissionService Permissions) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// Gets or sets the slug of the article to delete, from the route (with or without its <c>file:</c> prefix) or the form.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string UrlSlug { get; set; } = string.Empty;

	/// <summary>
	/// Gets the current title of the article being deleted, shown in the question.
	/// </summary>
	public string ArticleTitle { get; private set; } = string.Empty;

	/// <summary>
	/// Gets the article's own path, for the Cancel link.
	/// </summary>
	public string ArticlePath { get; private set; } = "/";

	/// <summary>
	/// Shows the confirmation for an article the account may delete.
	/// </summary>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>The page; 404 when the article does not exist; 403 when the account may not delete it.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		AllowSearchEngineIndexingOfPage = false;
		ArticleRevision? Article = await FindCurrentAsync(CancellationToken);
		if (Article is null) return NotFound();
		if (!await Permissions.MayDeleteArticleAsync(User, Article.UrlSlug, CancellationToken)) return Forbid();

		ArticleTitle = Article.Title;
		ArticlePath = ArticleUrlHelper.BuildArticlePath(Article);
		return Page();
	}

	/// <summary>
	/// Deletes the article (a soft delete of every revision) and sends the account to the home page, since the article
	/// page no longer exists.
	/// </summary>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>A redirect to the home page; 404 when the article does not exist; 403 when the account may not delete it.</returns>
	public async Task<IActionResult> OnPostAsync(CancellationToken CancellationToken)
	{
		Guid? CurrentUserId = PermissionService.GetUserId(User);
		if (CurrentUserId is null) return Challenge();

		try
		{
			bool MayDeleteArticles = await Permissions.HasPermissionAsync(User, Permission.DeleteArticles, CancellationToken);
			await ArticleRevisionService.DeleteArticleAsync(ArticleUrlHelper.NormalizeLookupSlug(UrlSlug), CurrentUserId.Value, MayDeleteArticles, CancellationToken);
		}
		catch (ArticleRevisionWorkflowException Ex) when (Ex.Kind == ArticleRevisionWorkflowFailureKind.NotFound)
		{
			return NotFound();
		}
		catch (ArticleRevisionWorkflowException Ex) when (Ex.Kind == ArticleRevisionWorkflowFailureKind.Forbidden)
		{
			return Forbid();
		}

		return Redirect("/");
	}

	/// <summary>
	/// Loads the current revision for <see cref="UrlSlug"/> on this site and culture.
	/// </summary>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>The current revision, or null when the slug is blank or no such article exists.</returns>
	private async Task<ArticleRevision?> FindCurrentAsync(CancellationToken CancellationToken)
	{
		if (string.IsNullOrWhiteSpace(UrlSlug)) return null;

		return await Context.ArticleRevisions
			.WithSpecification(new ArticleRevisionsBySlugSpec(ArticleUrlHelper.NormalizeLookupSlug(UrlSlug), IsCurrent: true))
			.AsNoTracking()
			.FirstOrDefaultAsync(CancellationToken);
	}
}
