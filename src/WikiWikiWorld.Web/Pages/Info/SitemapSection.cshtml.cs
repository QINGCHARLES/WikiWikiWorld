namespace WikiWikiWorld.Web.Pages.Info;

/// <summary>
/// Page model for sectioned and paginated HTML sitemap views under <c>/info:sitemap/{section}/...</c>.
/// </summary>
/// <param name="SitemapService">Loads bounded, index-backed HTML sitemap pages.</param>
/// <param name="SiteResolverService">Resolves the current site and culture.</param>
public sealed class SitemapSectionModel(
	ISitemapService SitemapService,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// Gets the loaded section page data.
	/// </summary>
	public HtmlSitemapSectionPage SectionPage { get; private set; } = default!;

	/// <summary>
	/// Gets the ordered pagination tokens (<see langword="null"/> indicates an ellipsis).
	/// </summary>
	public IReadOnlyList<int?> PaginationTokens { get; private set; } = [];

	/// <summary>
	/// Handles GET requests for section, bucket, and paginated HTML sitemap routes.
	/// </summary>
	/// <param name="Section">The requested content section slug.</param>
	/// <param name="Bucket">The optional initial-character bucket slug.</param>
	/// <param name="PageNumber">The 1-based page number.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The rendered page, a canonical permanent redirect, or <c>404 Not Found</c>.</returns>
	public async Task<IActionResult> OnGetAsync(
		string Section,
		string? Bucket = null,
		int PageNumber = 1,
		CancellationToken CancellationToken = default)
	{
		if (PageNumber < 1)
		{
			return NotFound();
		}

		SitemapSectionInfo? MatchedSection = Services.SitemapService.FindSection(Section);
		if (MatchedSection is null)
		{
			return NotFound();
		}

		string? NormalizedBucket = null;
		if (!string.IsNullOrWhiteSpace(Bucket))
		{
			(string Slug, string Label)? MatchedBucket = Services.SitemapService.FindBucket(Bucket);
			if (MatchedBucket is null)
			{
				return NotFound();
			}

			NormalizedBucket = MatchedBucket.Value.Slug;
		}

		string CanonicalUrl = BuildPageUrl(MatchedSection.Slug, NormalizedBucket, PageNumber);
		string RequestPath = Request.Path.Value ?? string.Empty;
		if (!string.Equals(RequestPath, CanonicalUrl, StringComparison.Ordinal))
		{
			return RedirectPermanent(CanonicalUrl);
		}

		HtmlSitemapSectionPage? LoadedPage = await SitemapService.GetHtmlSectionPageAsync(
			SiteId,
			Culture,
			MatchedSection.Slug,
			NormalizedBucket,
			PageNumber,
			CancellationToken);

		if (LoadedPage is null)
		{
			return NotFound();
		}

		SectionPage = LoadedPage;
		PaginationTokens = BuildPaginationTokens(LoadedPage.PageNumber, LoadedPage.TotalPages);

		string BucketSuffix = LoadedPage.ActiveBucket is not null ? $" ({LoadedPage.ActiveBucket.Label})" : string.Empty;
		string PageSuffix = LoadedPage.PageNumber > 1 ? $" - Page {LoadedPage.PageNumber}" : string.Empty;
		ViewData["Title"] = $"{LoadedPage.Section.Title}{BucketSuffix}{PageSuffix} - Sitemap";
		MetaDescription = $"Browse {LoadedPage.Section.Title.ToLowerInvariant()}{BucketSuffix} on {SiteName} ({Culture}).";

		return Page();
	}

	/// <summary>
	/// Builds a clean path-based URL (without query strings) for a section, bucket, and page number.
	/// </summary>
	/// <param name="SectionSlug">The canonical lowercase section slug.</param>
	/// <param name="BucketSlug">The optional canonical lowercase bucket slug.</param>
	/// <param name="PageNumber">The 1-based page number.</param>
	/// <returns>The canonical path-based URL.</returns>
	public static string BuildPageUrl(string SectionSlug, string? BucketSlug, int PageNumber = 1)
	{
		string Base = string.IsNullOrEmpty(BucketSlug)
			? $"/info:sitemap/{SectionSlug}"
			: $"/info:sitemap/{SectionSlug}/{BucketSlug}";

		return PageNumber > 1
			? $"{Base}/page/{PageNumber.ToString(CultureInfo.InvariantCulture)}"
			: Base;
	}

	/// <summary>
	/// Builds a compact list of page numbers with <see langword="null"/> representing ellipses.
	/// </summary>
	/// <param name="CurrentPage">The active page number.</param>
	/// <param name="TotalPages">The total number of pages.</param>
	/// <returns>Ordered page numbers and ellipsis placeholders.</returns>
	private static IReadOnlyList<int?> BuildPaginationTokens(int CurrentPage, int TotalPages)
	{
		if (TotalPages <= 1)
		{
			return [];
		}

		SortedSet<int> PagesToShow = [1, TotalPages];
		for (int Page = Math.Max(1, CurrentPage - 2); Page <= Math.Min(TotalPages, CurrentPage + 2); Page++)
		{
			PagesToShow.Add(Page);
		}

		List<int?> Tokens = [];
		int? Previous = null;

		foreach (int Page in PagesToShow)
		{
			if (Previous is not null && Page - Previous.Value > 1)
			{
				Tokens.Add(null);
			}

			Tokens.Add(Page);
			Previous = Page;
		}

		return Tokens.AsReadOnly();
	}
}
