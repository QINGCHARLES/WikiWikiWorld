using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace WikiWikiWorld.Web.Pages.Article;

[Authorize] // ✅ Require login
public sealed class EditModel(IArticleRevisionRepository ArticleRevisionRepository, UserManager<ApplicationUser> UserManager, SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	private readonly UserManager<ApplicationUser> UserManager = UserManager;

	// ✅ The query string parameter remains `UrlSlug`
	[BindProperty(SupportsGet = true)]
	public string UrlSlug { get; set; } = string.Empty;

	// ✅ Now `OriginalUrlSlug` is bound and persists on postback
	[BindProperty]
	public string OriginalUrlSlug { get; set; } = string.Empty;

	[BindProperty]
	public string Title { get; set; } = string.Empty;

	// ✅ Added DisplayTitle property for multi-line display title
	[BindProperty]
	public string DisplayTitle { get; set; } = string.Empty;

	[BindProperty]
	public string ArticleText { get; set; } = string.Empty;

	[BindProperty]
	public ArticleType SelectedType { get; set; } = ArticleType.Article;

	public string ErrorMessage { get; private set; } = string.Empty;

	public List<ArticleType> AvailableArticleTypes { get; } = [.. Enum.GetValues<ArticleType>()
		.Where(Type => Type != ArticleType.User)];

	public async Task<IActionResult> OnGetAsync()
	{
		if (string.IsNullOrWhiteSpace(UrlSlug))
		{
			return BadRequest("Invalid article identifier.");
		}

		if (!User.Identity?.IsAuthenticated ?? true)
		{
			return Challenge();
		}

		// ✅ Fetch article using `UrlSlug`
		ArticleRevision? CurrentArticle = await ArticleRevisionRepository.GetCurrentBySiteIdCultureAndUrlSlugAsync(SiteId, Culture, UrlSlug);

		if (CurrentArticle is null)
		{
			return NotFound("Article not found.");
		}

		OriginalUrlSlug = CurrentArticle.UrlSlug; // ✅ Store the original slug
		Title = CurrentArticle.Title;
		DisplayTitle = CurrentArticle.DisplayTitle ?? string.Empty; // ✅ Set the DisplayTitle
		ArticleText = CurrentArticle.Text;
		SelectedType = CurrentArticle.Type;

		return Page();
	}

	public async Task<IActionResult> OnPostAsync()
	{
		if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(UrlSlug) || string.IsNullOrWhiteSpace(ArticleText))
		{
			ErrorMessage = "All fields are required.";
			return Page();
		}

		if (!User.Identity?.IsAuthenticated ?? true)
		{
			return Challenge();
		}

		// ✅ Fetch the article using `OriginalUrlSlug`, which now persists on postback
		ArticleRevision? CurrentArticle = await ArticleRevisionRepository
			.GetCurrentBySiteIdCultureAndUrlSlugAsync(SiteId, Culture, OriginalUrlSlug);

		if (CurrentArticle is null)
		{
			return NotFound("Article not found.");
		}

		// ✅ Check if the new URL Slug is already in use (excluding itself)
		if (!OriginalUrlSlug.Equals(UrlSlug, StringComparison.OrdinalIgnoreCase))
		{
			ArticleRevision? ExistingArticle = await ArticleRevisionRepository
				.GetCurrentBySiteIdCultureAndUrlSlugAsync(SiteId, Culture, UrlSlug);

			if (ExistingArticle is not null)
			{
				ErrorMessage = "An article with this URL Slug already exists.";
				return Page();
			}
		}

		// ✅ Get current user ID
		Guid? CurrentUserId = GetCurrentUserId();
		if (CurrentUserId is null)
		{
			return Challenge();
		}

		// ✅ Insert a new revision with the updated URL slug
		await ArticleRevisionRepository.InsertAsync(
			CanonicalArticleId: CurrentArticle.CanonicalArticleId,
			SiteId: SiteId,
			Culture: Culture,
			Title: Title,
			DisplayTitle: DisplayTitle, // ✅ Use the DisplayTitle from the form
			UrlSlug: UrlSlug, // ✅ Stores the new slug
			Type: SelectedType,
			CanonicalFileId: CurrentArticle.CanonicalFileId,
			Text: ArticleText,
			RevisionReason: "User edit",
			CreatedByUserId: CurrentUserId.Value
		);

		// ✅ Redirect to the new URL slug
		return Redirect($"/{UrlSlug}");
	}

	private Guid? GetCurrentUserId()
	{
		string? UserIdString = UserManager.GetUserId(User);
		return Guid.TryParse(UserIdString, out Guid ParsedId) ? ParsedId : null;
	}
}