using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Services;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Page model for deleting articles.
/// </summary>
/// <param name="UserManager">The user manager.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="ArticleRevisionService">The article revision workflow service.</param>
[Authorize]
public sealed class DeleteModel(UserManager<User> UserManager, SiteResolverService SiteResolverService, ArticleRevisionService ArticleRevisionService) : BasePageModel(SiteResolverService)
{
	private readonly UserManager<User> UserManager = UserManager;

	/// <summary>
	/// Gets or sets the URL slug of the article to delete.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string UrlSlug { get; set; } = string.Empty;

	/// <summary>
	/// Gets any error message generated during processing.
	/// </summary>
	public string ErrorMessage { get; private set; } = string.Empty;

	/// <summary>
	/// Handles the POST request to delete an article.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A redirect to the home page or page with errors.</returns>
	public async Task<IActionResult> OnPostAsync(CancellationToken CancellationToken)
	{
		if (string.IsNullOrWhiteSpace(UrlSlug))
		{
			ErrorMessage = "Invalid article identifier.";
			return Page();
		}

		if (!User.Identity?.IsAuthenticated ?? true)
		{
			return Challenge();
		}

		Guid? CurrentUserId = GetCurrentUserId();
		if (CurrentUserId is null)
		{
			return Challenge();
		}

		try
		{
			await ArticleRevisionService.DeleteArticleAsync(UrlSlug, CancellationToken);
		}
		catch (ArticleRevisionWorkflowException Ex) when (Ex.Kind == ArticleRevisionWorkflowFailureKind.NotFound)
		{
			return NotFound("Article not found.");
		}

		return Redirect("/");
	}

	/// <summary>
	/// Gets the current user's identifier.
	/// </summary>
	/// <returns>The current user's identifier, or null if it cannot be parsed.</returns>
	private Guid? GetCurrentUserId()
	{
		string? UserIdString = UserManager.GetUserId(User);
		return Guid.TryParse(UserIdString, out Guid ParsedId) ? ParsedId : null;
	}
}
