using AngleSharp.Dom;
using Markdig;
using WikiWikiWorld.MarkdigExtensions;
using WikiWikiWorld.Web.MarkdigExtensions;

namespace WikiWikiWorld.Web.Pages.Article;

public sealed class ViewModel(
	IArticleRevisionRepository ArticleRevisionRepository,
	IFileRevisionRepository FileRevisionRepository,
	IDownloadUrlsRepository DownloadUrlsRepository,
	IUserRepository UserRepository,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	[BindProperty(SupportsGet = true)]
	public string UrlSlug { get; set; } = string.Empty;

	[BindProperty(SupportsGet = true)]
	public string? Revision { get; set; }

	public ArticleRevision? DisplayedRevision { get; set; }
	public ArticleRevision? CurrentRevision { get; private set; }
	public bool IsViewingCurrentRevision { get; private set; } = false;
	public string ArticleRevisionHtml { get; set; } = string.Empty;

	// New property to hold the recent authors as a list of (username, profilePicGuid) tuples.
	public IReadOnlyList<ArticleAuthor> RecentAuthors { get; set; } = Array.Empty<ArticleAuthor>();

	public async Task<IActionResult> OnGetAsync()
	{
		if (SiteId < 1 || string.IsNullOrWhiteSpace(Culture) || string.IsNullOrWhiteSpace(UrlSlug))
		{
			return BadRequest("Invalid parameters.");
		}

		UrlSlug = UrlSlug.Replace("file:", string.Empty);

		ArticleRevision? SpecificRevision = null;

		// Check if a revision is specified
		if (!string.IsNullOrWhiteSpace(Revision) && TryParseRevisionDate(Revision, out DateTimeOffset RevisionDate))
		{
			(CurrentRevision, SpecificRevision) = await ArticleRevisionRepository
				.GetRevisionBySiteIdCultureUrlSlugAndDateAsync(SiteId, Culture, UrlSlug, RevisionDate);

			// ✅ Determine if this revision is the latest one
			IsViewingCurrentRevision = SpecificRevision is not null && SpecificRevision.DateCreated == CurrentRevision?.DateCreated;
		}
		else
		{
			CurrentRevision = await ArticleRevisionRepository
				.GetCurrentBySiteIdCultureAndUrlSlugAsync(SiteId, Culture, UrlSlug);
		}

		DisplayedRevision = SpecificRevision ?? CurrentRevision;

		if (DisplayedRevision is null)
		{
			return NotFound();
		}

		// Fetch recent authors (username and profile pic GUID) for this article.
		DateTimeOffset? MaxRevisionDate = IsViewingCurrentRevision ? null : DisplayedRevision.DateCreated;
		RecentAuthors = await ArticleRevisionRepository.GetRecentAuthorsForArticleAsync(
			DisplayedRevision.CanonicalArticleId,
			MaxRevisionDate);

		// Markdown processing
		ShortDescriptionExtension ShortDescriptionExtension = new(this);
 		ImageExtension ImageExtension = new(SiteId, Culture, ArticleRevisionRepository, FileRevisionRepository);
		HeaderImageExtension HeaderImageExtension = new(SiteId, Culture, ArticleRevisionRepository, FileRevisionRepository, this);
		DownloadsBoxExtension DownloadsBoxExtension = new(SiteId, DownloadUrlsRepository, UserRepository);
		PullQuoteExtension PullQuoteExtension = new();
		TestExtension TestExtension = new();

		List<Category> Categories = [];
		CategoriesExtension CategoriesExtension = new(Categories);
		CategoryExtension CategoryExtension = new(Categories);

		List<Footnote> Footnotes = [];
		FootnotesExtension FootnotesExtension = new(Footnotes);
		FootnoteExtension FootnoteExtension = new(Footnotes);

		Dictionary<string, Citation> Citations = [];
		CitationsExtension CitationsExtension = new(Citations);
		CitationExtension CitationExtension = new(Citations);

		PublicationIssueInfoboxExtension PublicationIssueInfoboxExtension = new(SiteId, Culture, ArticleRevisionRepository, FileRevisionRepository);
		CoverGridExtension CoverGridExtension = new(SiteId, Culture, ArticleRevisionRepository, FileRevisionRepository);

		MarkdownPipelineBuilder Builder = new MarkdownPipelineBuilder()
							.Use(ShortDescriptionExtension)
							.Use(TestExtension)
							.Use(ImageExtension)
							.Use(HeaderImageExtension)
							.Use(CategoriesExtension)
							.Use(CategoryExtension)
							.Use(FootnotesExtension)
							.Use(FootnoteExtension)
							.Use(CitationsExtension)
							.Use(CitationExtension)
							.Use(PublicationIssueInfoboxExtension)
							.Use(CoverGridExtension)
							.Use(DownloadsBoxExtension)
							.Use(PullQuoteExtension)
							.UseAdvancedExtensions();

		MarkdownPipeline Pipeline = Builder.Build();
		ArticleRevisionHtml = Markdown.ToHtml(DisplayedRevision.Text, Pipeline);

		return Page();
	}

	private static bool TryParseRevisionDate(string Revision, out DateTimeOffset DateTime)
	{
		DateTime = default;

		if (Revision.Length == 14)
		{
			if (DateTimeOffset.TryParseExact(
				Revision, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal, out DateTimeOffset ParsedDate14))
			{
				DateTime = ParsedDate14;
				return true;
			}
		}
		else if (Revision.Length >= 15 && Revision.Length <= 21)
		{
			string NormalizedRevision = Revision.PadRight(21, '0'); // Ensure proper format

			if (DateTimeOffset.TryParseExact(
				NormalizedRevision, "yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal, out DateTimeOffset ParsedDateFull))
			{
				DateTime = ParsedDateFull;
				return true;
			}
		}

		return false;
	}
}
