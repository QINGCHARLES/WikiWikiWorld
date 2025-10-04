namespace WikiWikiWorld.Web.Pages.Article;

public sealed class ArticleHistoryModel(IArticleRevisionRepository ArticleRevisionRepository, SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	// Bind query string parameters (SupportsGet enables binding on GET requests).
	[BindProperty(SupportsGet = true)]
	public string UrlSlug { get; set; } = string.Empty;

	// List of all article revisions.
	public IReadOnlyList<ArticleRevision> ArticleRevisions { get; private set; } = [];

	public async Task<IActionResult> OnGetAsync()
	{
		// Validate the query string parameter.
		if (SiteId <= 0 || string.IsNullOrWhiteSpace(Culture) || string.IsNullOrWhiteSpace(UrlSlug))
		{
			return BadRequest("Invalid parameters.");
		}

		// Retrieve all revisions for the article.
		ArticleRevisions = await ArticleRevisionRepository.GetAllRevisionsBySiteIdCultureAndUrlSlugAsync(SiteId, Culture, UrlSlug);

		if (ArticleRevisions.Count == 0)
		{
			return NotFound("No revisions found for this article.");
		}

		return Page();
	}

	/// <summary>
	/// Converts a DateTimeOffset to the 15-21 digit revision format.
	/// </summary>
	/// <param name="date">The DateTimeOffset to format.</param>
	/// <returns>A string representing the formatted revision timestamp.</returns>
	public static string FormatRevisionTimestamp(DateTimeOffset date)
	{
		return date.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture).TrimEnd('0');
	}
}
