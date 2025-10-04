using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace WikiWikiWorld.Web.Pages.Article;

[Authorize] // ✅ Requires authentication
public sealed class DeleteModel(IArticleRevisionRepository ArticleRevisionRepository, UserManager<ApplicationUser> UserManager, SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	private readonly UserManager<ApplicationUser> UserManager = UserManager;

	[BindProperty(SupportsGet = true)]
	public string UrlSlug { get; set; } = string.Empty;

	public string ErrorMessage { get; private set; } = string.Empty;

	public async Task<IActionResult> OnPostAsync()
	{
		if (string.IsNullOrWhiteSpace(UrlSlug))
		{
			ErrorMessage = "Invalid article identifier.";
			return Page();
		}

		// ✅ Ensure the user is logged in
		if (!User.Identity?.IsAuthenticated ?? true)
		{
			return Challenge();
		}

		// ✅ Fetch the article
		ArticleRevision? CurrentArticle = await ArticleRevisionRepository
			.GetCurrentBySiteIdCultureAndUrlSlugAsync(SiteId, Culture, UrlSlug);

		if (CurrentArticle is null)
		{
			return NotFound("Article not found.");
		}

		// ✅ Get current user ID
		Guid? CurrentUserId = GetCurrentUserId();
		if (CurrentUserId is null)
		{
			return Challenge();
		}

		// ✅ Perform a soft delete (sets DateDeleted on all revisions)
		await ArticleRevisionRepository.DeleteByCanonicalIdAsync(SiteId, Culture, CurrentArticle.CanonicalArticleId);

		// ✅ Redirect after deletion
		return Redirect("/");
	}

	// ✅ Helper method to get current user ID
	private Guid? GetCurrentUserId()
	{
		string? UserIdString = UserManager.GetUserId(User);
		return Guid.TryParse(UserIdString, out Guid ParsedId) ? ParsedId : null;
	}
}
