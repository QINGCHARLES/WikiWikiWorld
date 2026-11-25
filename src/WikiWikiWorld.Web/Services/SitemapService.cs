using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Describes a top-level content section in the HTML and XML sitemaps.
/// </summary>
/// <param name="Slug">The lowercase URL segment identifying the section.</param>
/// <param name="Title">The user-facing heading for the section.</param>
/// <param name="Description">A brief description of the entries in the section.</param>
/// <param name="ArticleType">The backing <see cref="WikiWikiWorld.Data.Models.ArticleType"/>.</param>
/// <param name="IncludeInXml">Whether entries in this section are included in XML sitemaps for search engines.</param>
public sealed record SitemapSectionInfo(
	string Slug,
	string Title,
	string Description,
	ArticleType ArticleType,
	bool IncludeInXml = true);

/// <summary>
/// Represents an initial-character bucket within an HTML sitemap section.
/// </summary>
/// <param name="Slug">The lowercase URL segment for the bucket (<c>a</c>–<c>z</c>, <c>0-9</c>, or <c>other</c>).</param>
/// <param name="Label">The user-facing label for the bucket.</param>
/// <param name="HasEntries">Whether the bucket currently contains at least one entry.</param>
public sealed record SitemapBucketLink(string Slug, string Label, bool HasEntries);

/// <summary>
/// Summarizes one content section on the HTML sitemap hub page.
/// </summary>
/// <param name="Section">The section metadata.</param>
/// <param name="TotalCount">The total number of current entries in this section.</param>
/// <param name="Buckets">The ordered initial-character buckets and their availability.</param>
public sealed record SitemapHubSection(
	SitemapSectionInfo Section,
	int TotalCount,
	IReadOnlyList<SitemapBucketLink> Buckets);

/// <summary>
/// Represents a static utility link displayed on the HTML sitemap hub and included in the XML sitemap.
/// </summary>
/// <param name="Label">The user-facing link text.</param>
/// <param name="Path">The relative path on the current host.</param>
public sealed record SitemapStaticLink(string Label, string Path);

/// <summary>
/// Represents a link to another language edition of the current site.
/// </summary>
/// <param name="CultureCode">The culture code.</param>
/// <param name="DisplayName">The localized endonym for the language.</param>
/// <param name="Url">The absolute URL to the language edition.</param>
public sealed record SitemapCultureLink(string CultureCode, string DisplayName, string Url);

/// <summary>
/// Contains the full data needed to render the <c>/info:sitemap</c> hub page.
/// </summary>
/// <param name="StaticLinks">Site-wide utility and policy links.</param>
/// <param name="CultureLinks">Other language editions configured for the current site.</param>
/// <param name="Sections">Content sections with counts and bucket availability.</param>
/// <param name="TotalContentCount">The sum of all content entries across sections.</param>
public sealed record HtmlSitemapHub(
	IReadOnlyList<SitemapStaticLink> StaticLinks,
	IReadOnlyList<SitemapCultureLink> CultureLinks,
	IReadOnlyList<SitemapHubSection> Sections,
	int TotalContentCount);

/// <summary>
/// Represents a single article link within an HTML sitemap section page.
/// </summary>
/// <param name="Title">The plain-text display title.</param>
/// <param name="Path">The canonical site-relative URL path.</param>
public sealed record HtmlSitemapEntry(string Title, string Path);

/// <summary>
/// Contains one paginated slice of entries for an HTML sitemap section or bucket view.
/// </summary>
/// <param name="Section">The resolved section metadata.</param>
/// <param name="ActiveBucket">The active bucket, or <see langword="null"/> when showing all entries in the section.</param>
/// <param name="Buckets">All buckets in the section and whether each has entries.</param>
/// <param name="Entries">The entries on the current page.</param>
/// <param name="PageNumber">The 1-based current page number.</param>
/// <param name="TotalPages">The total number of pages for the current filter (at least 1).</param>
/// <param name="TotalItems">The total number of matching entries for the current filter.</param>
public sealed record HtmlSitemapSectionPage(
	SitemapSectionInfo Section,
	SitemapBucketLink? ActiveBucket,
	IReadOnlyList<SitemapBucketLink> Buckets,
	IReadOnlyList<HtmlSitemapEntry> Entries,
	int PageNumber,
	int TotalPages,
	int TotalItems);

/// <summary>
/// Defines operations for generating scalable XML and HTML sitemaps.
/// </summary>
public interface ISitemapService
{
	/// <summary>
	/// Generates the root XML sitemap (<c>/sitemap.xml</c>): either a single <c>&lt;urlset&gt;</c>
	/// or a <c>&lt;sitemapindex&gt;</c> when total URLs exceed the per-file protocol limit.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The sitemap XML string.</returns>
	Task<string> GenerateSitemapAsync(CancellationToken CancellationToken = default);

	/// <summary>
	/// Generates the static-pages XML <c>&lt;urlset&gt;</c> (<c>/sitemaps/static.xml</c>).
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The XML string, or <see langword="null"/> on culture-selector root domains.</returns>
	Task<string?> GenerateStaticSitemapAsync(CancellationToken CancellationToken = default);

	/// <summary>
	/// Generates a chunked XML <c>&lt;urlset&gt;</c> for a specific content section and 1-based page number
	/// (<c>/sitemaps/{section}-{page}.xml</c>).
	/// </summary>
	/// <param name="SectionSlug">The section slug (for example, <c>articles</c>).</param>
	/// <param name="PageNumber">The 1-based chunk number.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The XML string, or <see langword="null"/> when the section or chunk does not exist.</returns>
	Task<string?> GenerateSectionSitemapAsync(string SectionSlug, int PageNumber, CancellationToken CancellationToken = default);

	/// <summary>
	/// Loads the cached hub summary for <c>/info:sitemap</c>.
	/// </summary>
	/// <param name="SiteId">The current site ID.</param>
	/// <param name="Culture">The current culture code.</param>
	/// <param name="Scheme">The request scheme.</param>
	/// <param name="Host">The request host name.</param>
	/// <param name="Port">The optional request port.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The HTML sitemap hub data.</returns>
	Task<HtmlSitemapHub> GetHtmlHubAsync(int SiteId, string Culture, string Scheme, string Host, int? Port, CancellationToken CancellationToken = default);

	/// <summary>
	/// Loads one paginated page of entries for <c>/info:sitemap/{section}</c> or <c>/info:sitemap/{section}/{bucket}</c>.
	/// </summary>
	/// <param name="SiteId">The current site ID.</param>
	/// <param name="Culture">The current culture code.</param>
	/// <param name="SectionSlug">The section slug.</param>
	/// <param name="BucketSlug">The optional initial bucket slug.</param>
	/// <param name="PageNumber">The 1-based page number.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The section page data, or <see langword="null"/> when the section, bucket, or page number is invalid or out of range.</returns>
	Task<HtmlSitemapSectionPage?> GetHtmlSectionPageAsync(
		int SiteId,
		string Culture,
		string SectionSlug,
		string? BucketSlug,
		int PageNumber,
		CancellationToken CancellationToken = default);
}

/// <summary>
/// Generates index-backed XML sitemaps (with automatic <c>&lt;sitemapindex&gt;</c> chunking)
/// and sectioned, paginated HTML sitemaps.
/// </summary>
public sealed class SitemapService : ISitemapService
{
	/// <summary>
	/// Maximum URLs permitted in a single XML sitemap file per the Sitemaps protocol.
	/// </summary>
	public const int DefaultMaxXmlUrlsPerFile = 50_000;

	/// <summary>
	/// Number of links displayed per HTML sitemap page.
	/// </summary>
	public const int HtmlPageSize = 200;

	private const string SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";
	// Saving an article does not evict these caches, so the duration is how long a new article can be missing.
	private static readonly TimeSpan XmlCacheDuration = TimeSpan.FromHours(1);
	private static readonly TimeSpan HtmlCacheDuration = TimeSpan.FromHours(1);

	// Section titles, descriptions and link labels are English translation keys; pages show them through L[...].
	// l10n: "Articles", "Encyclopedia articles and publication entries", "Categories", "Topic and index category pages", "Files", "Uploaded images, covers, and media pages", "Info Pages", "Reference, guide, and policy pages", "Redirects", "Redirect pages pointing to canonical articles"
	// l10n: "Home", "HTML Sitemap", "Search", "Recent Changes", "Copyright & DMCA", "Terms of Use", "Privacy Policy", "Other"
	private static readonly IReadOnlyList<SitemapSectionInfo> OrderedSections =
	[
		new("articles", "Articles", "Encyclopedia articles and publication entries", ArticleType.Article),
		new("categories", "Categories", "Topic and index category pages", ArticleType.Category),
		new("files", "Files", "Uploaded images, covers, and media pages", ArticleType.File),
		new("info", "Info Pages", "Reference, guide, and policy pages", ArticleType.Info),
		new("redirects", "Redirects", "Redirect pages pointing to canonical articles", ArticleType.Redirect, IncludeInXml: false)
	];

	private static readonly IReadOnlyList<SitemapStaticLink> StaticUtilityLinks =
	[
		new("Home", "/"),
		new("HTML Sitemap", "/info:sitemap"),
		new("Search", "/search:"),
		new("Recent Changes", "/firehose:"),
		new("Copyright & DMCA", "/dmca:"),
		new("Terms of Use", "/terms:"),
		new("Privacy Policy", "/privacy-policy:")
	];

	private static readonly IReadOnlyList<(string Slug, string Label)> BucketDefinitions = BuildBucketDefinitions();

	private readonly WikiWikiWorldDbContext Context;
	private readonly SiteResolverService SiteResolver;
	private readonly IMemoryCache MemoryCache;
	private readonly IHttpContextAccessor HttpContextAccessor;
	private readonly SiteConfiguration SiteConfiguration;
	private readonly int MaxXmlUrlsPerFile;

	/// <summary>
	/// Initializes a new instance of the <see cref="SitemapService"/> class.
	/// </summary>
	/// <param name="Context">The database context.</param>
	/// <param name="SiteResolver">The site resolver service.</param>
	/// <param name="MemoryCache">The memory cache.</param>
	/// <param name="HttpContextAccessor">The HTTP context accessor.</param>
	/// <param name="SiteConfigurationOptions">The configured site definitions.</param>
	public SitemapService(
		WikiWikiWorldDbContext Context,
		SiteResolverService SiteResolver,
		IMemoryCache MemoryCache,
		IHttpContextAccessor HttpContextAccessor,
		IOptions<SiteConfiguration> SiteConfigurationOptions)
		: this(Context, SiteResolver, MemoryCache, HttpContextAccessor, SiteConfigurationOptions, DefaultMaxXmlUrlsPerFile)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="SitemapService"/> class with a custom XML chunk size for testing.
	/// </summary>
	/// <param name="Context">The database context.</param>
	/// <param name="SiteResolver">The site resolver service.</param>
	/// <param name="MemoryCache">The memory cache.</param>
	/// <param name="HttpContextAccessor">The HTTP context accessor.</param>
	/// <param name="SiteConfigurationOptions">The configured site definitions.</param>
	/// <param name="MaxXmlUrlsPerFile">The maximum number of URLs per XML sitemap file.</param>
	internal SitemapService(
		WikiWikiWorldDbContext Context,
		SiteResolverService SiteResolver,
		IMemoryCache MemoryCache,
		IHttpContextAccessor HttpContextAccessor,
		IOptions<SiteConfiguration> SiteConfigurationOptions,
		int MaxXmlUrlsPerFile)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(MaxXmlUrlsPerFile, 1);

		this.Context = Context;
		this.SiteResolver = SiteResolver;
		this.MemoryCache = MemoryCache;
		this.HttpContextAccessor = HttpContextAccessor;
		this.SiteConfiguration = SiteConfigurationOptions.Value;
		this.MaxXmlUrlsPerFile = MaxXmlUrlsPerFile;
	}

	/// <summary>
	/// Resolves section metadata for a route slug.
	/// </summary>
	/// <param name="SectionSlug">The incoming section route slug.</param>
	/// <returns>The matching section metadata, or <see langword="null"/> if unknown.</returns>
	public static SitemapSectionInfo? FindSection(string? SectionSlug)
	{
		if (string.IsNullOrWhiteSpace(SectionSlug))
		{
			return null;
		}

		return OrderedSections.FirstOrDefault(S => S.Slug.Equals(SectionSlug, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Resolves bucket metadata for a route slug.
	/// </summary>
	/// <param name="BucketSlug">The incoming bucket route slug.</param>
	/// <returns>The matching bucket slug and display label, or <see langword="null"/> if unknown.</returns>
	public static (string Slug, string Label)? FindBucket(string? BucketSlug)
	{
		if (string.IsNullOrWhiteSpace(BucketSlug))
		{
			return null;
		}

		foreach ((string Slug, string Label) Bucket in BucketDefinitions)
		{
			if (Bucket.Slug.Equals(BucketSlug, StringComparison.OrdinalIgnoreCase))
			{
				return Bucket;
			}
		}

		return null;
	}

	/// <inheritdoc/>
	public async Task<string> GenerateSitemapAsync(CancellationToken CancellationToken = default)
	{
		string BaseUrl = GetBaseUrl();

		try
		{
			(int SiteId, string Culture, bool IsCultureSelectorRootDomain) = SiteResolver.ResolveSiteAndCultureWithRootCheck();
			if (IsCultureSelectorRootDomain)
			{
				return BuildRootUrlSetXml(BaseUrl);
			}

			string CacheKey = $"Sitemap_Xml_Root_{SiteId}_{Culture}_{BaseUrl}_{MaxXmlUrlsPerFile}";
			if (MemoryCache.TryGetValue(CacheKey, out string? CachedXml) && CachedXml is not null)
			{
				return CachedXml;
			}

			Dictionary<ArticleType, int> CountsByType = await GetArticleCountsByTypeAsync(SiteId, Culture, IncludeNonXmlSections: false, CancellationToken);
			int TotalArticleCount = CountsByType.Values.Sum();
			int TotalUrlCount = StaticUtilityLinks.Count + TotalArticleCount;

			string GeneratedXml = TotalUrlCount <= MaxXmlUrlsPerFile
				? await BuildSingleUrlSetXmlAsync(SiteId, Culture, BaseUrl, CancellationToken)
				: BuildSitemapIndexXml(BaseUrl, CountsByType);

			MemoryCache.Set(CacheKey, GeneratedXml, new MemoryCacheEntryOptions
			{
				AbsoluteExpirationRelativeToNow = XmlCacheDuration,
				Priority = CacheItemPriority.Normal
			});

			return GeneratedXml;
		}
		catch (InvalidOperationException)
		{
			return BuildRootUrlSetXml(BaseUrl);
		}
	}

	/// <inheritdoc/>
	public Task<string?> GenerateStaticSitemapAsync(CancellationToken CancellationToken = default)
	{
		CancellationToken.ThrowIfCancellationRequested();
		string BaseUrl = GetBaseUrl();

		try
		{
			(int SiteId, string Culture, bool IsCultureSelectorRootDomain) = SiteResolver.ResolveSiteAndCultureWithRootCheck();
			if (IsCultureSelectorRootDomain)
			{
				return Task.FromResult<string?>(null);
			}

			string CacheKey = $"Sitemap_Xml_Static_{SiteId}_{Culture}_{BaseUrl}";
			if (MemoryCache.TryGetValue(CacheKey, out string? CachedXml) && CachedXml is not null)
			{
				return Task.FromResult<string?>(CachedXml);
			}

			string GeneratedXml = BuildStaticUrlSetXml(BaseUrl);
			MemoryCache.Set(CacheKey, GeneratedXml, new MemoryCacheEntryOptions
			{
				AbsoluteExpirationRelativeToNow = XmlCacheDuration,
				Priority = CacheItemPriority.Normal
			});

			return Task.FromResult<string?>(GeneratedXml);
		}
		catch (InvalidOperationException)
		{
			return Task.FromResult<string?>(null);
		}
	}

	/// <inheritdoc/>
	public async Task<string?> GenerateSectionSitemapAsync(string SectionSlug, int PageNumber, CancellationToken CancellationToken = default)
	{
		SitemapSectionInfo? Section = FindSection(SectionSlug);
		if (Section is null || !Section.IncludeInXml || PageNumber < 1)
		{
			return null;
		}

		string BaseUrl = GetBaseUrl();

		try
		{
			(int SiteId, string Culture, bool IsCultureSelectorRootDomain) = SiteResolver.ResolveSiteAndCultureWithRootCheck();
			if (IsCultureSelectorRootDomain)
			{
				return null;
			}

			IReadOnlyList<XmlChunkBoundary> Boundaries = await GetXmlChunkBoundariesAsync(SiteId, Culture, Section.ArticleType, CancellationToken);
			if (PageNumber > Boundaries.Count)
			{
				return null;
			}

			XmlChunkBoundary Range = Boundaries[PageNumber - 1];
			List<XmlArticleProjection> Items = await Context.ArticleRevisions
				.AsNoTracking()
				.Where(r => r.SiteId == SiteId
					&& r.Culture == Culture
					&& r.IsCurrent
					&& r.Type == Section.ArticleType
					&& r.Id >= Range.StartIdInclusive
					&& r.Id <= Range.EndIdInclusive)
				.OrderBy(r => r.Id)
				.Select(r => new XmlArticleProjection(r.UrlSlug, r.Type, r.DateCreated))
				.ToListAsync(CancellationToken);

			return BuildArticleUrlSetXml(BaseUrl, Items);
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	/// <inheritdoc/>
	public async Task<HtmlSitemapHub> GetHtmlHubAsync(
		int SiteId,
		string Culture,
		string Scheme,
		string Host,
		int? Port,
		CancellationToken CancellationToken = default)
	{
		IReadOnlyList<SitemapHubSection> Sections = await GetCachedHubSectionsAsync(SiteId, Culture, CancellationToken);
		IReadOnlyList<SitemapCultureLink> CultureLinks = BuildCultureLinks(SiteId, Culture, Scheme, Host, Port);
		int TotalContentCount = Sections.Sum(S => S.TotalCount);

		return new HtmlSitemapHub(StaticUtilityLinks, CultureLinks, Sections, TotalContentCount);
	}

	/// <inheritdoc/>
	public async Task<HtmlSitemapSectionPage?> GetHtmlSectionPageAsync(
		int SiteId,
		string Culture,
		string SectionSlug,
		string? BucketSlug,
		int PageNumber,
		CancellationToken CancellationToken = default)
	{
		SitemapSectionInfo? Section = FindSection(SectionSlug);
		if (Section is null || PageNumber < 1)
		{
			return null;
		}

		SitemapBucketLink? ActiveBucket = null;
		string? NormalizedBucket = null;

		if (!string.IsNullOrWhiteSpace(BucketSlug))
		{
			(string Slug, string Label)? MatchedBucket = FindBucket(BucketSlug);
			if (MatchedBucket is null)
			{
				return null;
			}

			NormalizedBucket = MatchedBucket.Value.Slug;
		}

		IReadOnlyList<SitemapHubSection> HubSections = await GetCachedHubSectionsAsync(SiteId, Culture, CancellationToken);
		IReadOnlyList<SitemapBucketLink> SectionBuckets = HubSections
			.FirstOrDefault(S => S.Section.ArticleType == Section.ArticleType)?.Buckets
			?? BuildEmptyBuckets();

		if (NormalizedBucket is not null)
		{
			ActiveBucket = SectionBuckets.First(B => B.Slug == NormalizedBucket);
		}

		IQueryable<ArticleRevision> FilteredQuery = ApplyBucketFilter(
			Context.ArticleRevisions
				.AsNoTracking()
				.Where(r => r.SiteId == SiteId && r.Culture == Culture && r.IsCurrent && r.Type == Section.ArticleType),
			NormalizedBucket);

		int TotalItems = await FilteredQuery.CountAsync(CancellationToken);
		int TotalPages = Math.Max(1, (TotalItems + HtmlPageSize - 1) / HtmlPageSize);
		if (PageNumber > TotalPages)
		{
			return null;
		}

		List<HtmlRawProjection> RawEntries = TotalItems == 0
			? []
			: await FilteredQuery
				.OrderBy(r => EF.Functions.Collate(r.Title, "NOCASE"))
				.ThenBy(r => r.Id)
				.Skip((PageNumber - 1) * HtmlPageSize)
				.Take(HtmlPageSize)
				.Select(r => new HtmlRawProjection(r.UrlSlug, r.Title, r.Type))
				.ToListAsync(CancellationToken);

		List<HtmlSitemapEntry> Entries =
		[
			.. RawEntries.Select(Entry => new HtmlSitemapEntry(
				Entry.Title,
				ArticleUrlHelper.BuildArticlePath(Entry.UrlSlug, Entry.Type)))
		];

		return new HtmlSitemapSectionPage(
			Section,
			ActiveBucket,
			SectionBuckets,
			Entries,
			PageNumber,
			TotalPages,
			TotalItems);
	}

	/// <summary>
	/// Queries and caches section counts and per-bucket existence flags for a site and culture.
	/// </summary>
	/// <param name="SiteId">The site ID.</param>
	/// <param name="Culture">The culture code.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>Ordered section summaries.</returns>
	private async Task<IReadOnlyList<SitemapHubSection>> GetCachedHubSectionsAsync(int SiteId, string Culture, CancellationToken CancellationToken)
	{
		string CacheKey = $"Sitemap_Html_Hub_{SiteId}_{Culture}";
		if (MemoryCache.TryGetValue(CacheKey, out IReadOnlyList<SitemapHubSection>? CachedSections) && CachedSections is not null)
		{
			return CachedSections;
		}

		Dictionary<ArticleType, int> CountsByType = await GetArticleCountsByTypeAsync(SiteId, Culture, IncludeNonXmlSections: true, CancellationToken);
		List<SitemapHubSection> Sections = new(OrderedSections.Count);

		foreach (SitemapSectionInfo Section in OrderedSections)
		{
			int Count = CountsByType.GetValueOrDefault(Section.ArticleType, 0);
			IReadOnlyList<SitemapBucketLink> Buckets = Count > 0
				? await ProbeBucketsAsync(SiteId, Culture, Section.ArticleType, CancellationToken)
				: BuildEmptyBuckets();

			Sections.Add(new SitemapHubSection(Section, Count, Buckets));
		}

		IReadOnlyList<SitemapHubSection> ReadonlySections = Sections.AsReadOnly();
		MemoryCache.Set(CacheKey, ReadonlySections, new MemoryCacheEntryOptions
		{
			AbsoluteExpirationRelativeToNow = HtmlCacheDuration,
			Priority = CacheItemPriority.Normal
		});

		return ReadonlySections;
	}

	/// <summary>
	/// Probes each initial-character bucket using index-backed <c>ANY</c> seeks on <c>IX_ArticleRevisions_Sitemap_Html</c>.
	/// </summary>
	/// <param name="SiteId">The site ID.</param>
	/// <param name="Culture">The culture code.</param>
	/// <param name="Type">The article type.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The bucket list with existence flags.</returns>
	private async Task<IReadOnlyList<SitemapBucketLink>> ProbeBucketsAsync(
		int SiteId,
		string Culture,
		ArticleType Type,
		CancellationToken CancellationToken)
	{
		IQueryable<ArticleRevision> BaseQuery = Context.ArticleRevisions
			.AsNoTracking()
			.Where(r => r.SiteId == SiteId && r.Culture == Culture && r.IsCurrent && r.Type == Type);

		List<SitemapBucketLink> Results = new(BucketDefinitions.Count);
		foreach ((string Slug, string Label) Bucket in BucketDefinitions)
		{
			bool HasEntries = await ApplyBucketFilter(BaseQuery, Bucket.Slug).AnyAsync(CancellationToken);
			Results.Add(new SitemapBucketLink(Bucket.Slug, Bucket.Label, HasEntries));
		}

		return Results.AsReadOnly();
	}

	/// <summary>
	/// Counts current indexable articles grouped by <see cref="ArticleType"/> using <c>IX_ArticleRevisions_Sitemap_Xml</c>.
	/// </summary>
	/// <param name="SiteId">The site ID.</param>
	/// <param name="Culture">The culture code.</param>
	/// <param name="IncludeNonXmlSections">Whether sections excluded from XML sitemaps (such as redirects) are included in the counts.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>Counts keyed by <see cref="ArticleType"/>.</returns>
	private async Task<Dictionary<ArticleType, int>> GetArticleCountsByTypeAsync(
		int SiteId,
		string Culture,
		bool IncludeNonXmlSections,
		CancellationToken CancellationToken)
	{
		IQueryable<ArticleRevision> Query = Context.ArticleRevisions
			.AsNoTracking()
			.Where(r => r.SiteId == SiteId && r.Culture == Culture && r.IsCurrent && r.Type != ArticleType.User);

		if (!IncludeNonXmlSections)
		{
			Query = Query.Where(r => r.Type != ArticleType.Redirect);
		}

		var Rows = await Query
			.GroupBy(r => r.Type)
			.Select(g => new { Type = g.Key, Count = g.Count() })
			.ToListAsync(CancellationToken);

		return Rows.ToDictionary(R => R.Type, R => R.Count);
	}

	/// <summary>
	/// Computes and caches keyset <c>Id</c> boundaries for XML chunk files without cumulative offsets.
	/// </summary>
	/// <param name="SiteId">The site ID.</param>
	/// <param name="Culture">The culture code.</param>
	/// <param name="Type">The article type.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>Ordered chunk ID ranges.</returns>
	private async Task<IReadOnlyList<XmlChunkBoundary>> GetXmlChunkBoundariesAsync(
		int SiteId,
		string Culture,
		ArticleType Type,
		CancellationToken CancellationToken)
	{
		string CacheKey = $"Sitemap_Xml_Chunks_{SiteId}_{Culture}_{Type}_{MaxXmlUrlsPerFile}";
		if (MemoryCache.TryGetValue(CacheKey, out IReadOnlyList<XmlChunkBoundary>? Cached) && Cached is not null)
		{
			return Cached;
		}

		IQueryable<ArticleRevision> BaseQuery = Context.ArticleRevisions
			.AsNoTracking()
			.Where(r => r.SiteId == SiteId && r.Culture == Culture && r.IsCurrent && r.Type == Type);

		List<XmlChunkBoundary> Boundaries = [];
		int LastEndId = 0;

		while (true)
		{
			int? StartId = await BaseQuery
				.Where(r => r.Id > LastEndId)
				.OrderBy(r => r.Id)
				.Select(r => (int?)r.Id)
				.FirstOrDefaultAsync(CancellationToken);

			if (StartId is null)
			{
				break;
			}

			int? EndId = MaxXmlUrlsPerFile == 1
				? StartId
				: await BaseQuery
					.Where(r => r.Id > LastEndId)
					.OrderBy(r => r.Id)
					.Skip(MaxXmlUrlsPerFile - 1)
					.Select(r => (int?)r.Id)
					.FirstOrDefaultAsync(CancellationToken);

			if (EndId is null)
			{
				int? MaxId = await BaseQuery
					.Where(r => r.Id > LastEndId)
					.MaxAsync(r => (int?)r.Id, CancellationToken);

				Boundaries.Add(new XmlChunkBoundary(StartId.Value, MaxId!.Value));
				break;
			}

			Boundaries.Add(new XmlChunkBoundary(StartId.Value, EndId.Value));
			LastEndId = EndId.Value;
		}

		IReadOnlyList<XmlChunkBoundary> ReadonlyBoundaries = Boundaries.AsReadOnly();
		MemoryCache.Set(CacheKey, ReadonlyBoundaries, new MemoryCacheEntryOptions
		{
			AbsoluteExpirationRelativeToNow = XmlCacheDuration,
			Priority = CacheItemPriority.Normal
		});

		return ReadonlyBoundaries;
	}

	/// <summary>
	/// Builds a single XML <c>&lt;urlset&gt;</c> containing static utility links and all current non-user articles.
	/// </summary>
	/// <param name="SiteId">The site ID.</param>
	/// <param name="Culture">The culture code.</param>
	/// <param name="BaseUrl">The scheme and host prefix.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The serialized XML document.</returns>
	private async Task<string> BuildSingleUrlSetXmlAsync(int SiteId, string Culture, string BaseUrl, CancellationToken CancellationToken)
	{
		List<XmlArticleProjection> Entries = await Context.ArticleRevisions
			.AsNoTracking()
			.Where(r => r.SiteId == SiteId
				&& r.Culture == Culture
				&& r.IsCurrent
				&& r.Type != ArticleType.User
				&& r.Type != ArticleType.Redirect)
			.OrderBy(r => r.Id)
			.Select(r => new XmlArticleProjection(r.UrlSlug, r.Type, r.DateCreated))
			.ToListAsync(CancellationToken);

		return WriteXml(Writer =>
		{
			Writer.WriteStartElement("urlset", SitemapNamespace);
			foreach (SitemapStaticLink StaticLink in StaticUtilityLinks)
			{
				WriteStaticUrlElement(Writer, BaseUrl, StaticLink.Path);
			}

			foreach (XmlArticleProjection Entry in Entries)
			{
				WriteArticleUrlElement(Writer, BaseUrl, Entry);
			}

			Writer.WriteEndElement();
		});
	}

	/// <summary>
	/// Builds a <c>&lt;sitemapindex&gt;</c> pointing to <c>/sitemaps/static.xml</c> and per-section chunk files.
	/// </summary>
	/// <param name="BaseUrl">The scheme and host prefix.</param>
	/// <param name="CountsByType">Current counts per article type.</param>
	/// <returns>The serialized XML sitemap index document.</returns>
	private string BuildSitemapIndexXml(string BaseUrl, IReadOnlyDictionary<ArticleType, int> CountsByType)
	{
		return WriteXml(Writer =>
		{
			Writer.WriteStartElement("sitemapindex", SitemapNamespace);

			WriteSitemapLocElement(Writer, $"{BaseUrl}/sitemaps/static.xml");

			foreach (SitemapSectionInfo Section in OrderedSections)
			{
				if (!Section.IncludeInXml)
				{
					continue;
				}

				int Count = CountsByType.GetValueOrDefault(Section.ArticleType, 0);
				if (Count == 0)
				{
					continue;
				}

				int ChunkCount = (Count + MaxXmlUrlsPerFile - 1) / MaxXmlUrlsPerFile;
				for (int Chunk = 1; Chunk <= ChunkCount; Chunk++)
				{
					WriteSitemapLocElement(Writer, $"{BaseUrl}/sitemaps/{Section.Slug}-{Chunk.ToString(CultureInfo.InvariantCulture)}.xml");
				}
			}

			Writer.WriteEndElement();
		});
	}

	/// <summary>
	/// Builds the minimal XML <c>&lt;urlset&gt;</c> for culture-selector root domains.
	/// </summary>
	/// <param name="BaseUrl">The base URL.</param>
	/// <returns>The serialized XML document.</returns>
	private static string BuildRootUrlSetXml(string BaseUrl)
	{
		return WriteXml(Writer =>
		{
			Writer.WriteStartElement("urlset", SitemapNamespace);
			WriteStaticUrlElement(Writer, BaseUrl, "/");
			Writer.WriteEndElement();
		});
	}

	/// <summary>
	/// Builds the XML <c>&lt;urlset&gt;</c> for static utility routes.
	/// </summary>
	/// <param name="BaseUrl">The base URL.</param>
	/// <returns>The serialized XML document.</returns>
	private static string BuildStaticUrlSetXml(string BaseUrl)
	{
		return WriteXml(Writer =>
		{
			Writer.WriteStartElement("urlset", SitemapNamespace);
			foreach (SitemapStaticLink StaticLink in StaticUtilityLinks)
			{
				WriteStaticUrlElement(Writer, BaseUrl, StaticLink.Path);
			}

			Writer.WriteEndElement();
		});
	}

	/// <summary>
	/// Builds an XML <c>&lt;urlset&gt;</c> for a slice of article entries.
	/// </summary>
	/// <param name="BaseUrl">The base URL.</param>
	/// <param name="Entries">The projected article entries.</param>
	/// <returns>The serialized XML document.</returns>
	private static string BuildArticleUrlSetXml(string BaseUrl, IReadOnlyList<XmlArticleProjection> Entries)
	{
		return WriteXml(Writer =>
		{
			Writer.WriteStartElement("urlset", SitemapNamespace);
			foreach (XmlArticleProjection Entry in Entries)
			{
				WriteArticleUrlElement(Writer, BaseUrl, Entry);
			}

			Writer.WriteEndElement();
		});
	}

	/// <summary>
	/// Applies an optional initial-character bucket range filter using <c>COLLATE NOCASE</c> index bounds.
	/// </summary>
	/// <param name="Query">The base query.</param>
	/// <param name="BucketSlug">The normalized bucket slug, or <see langword="null"/>.</param>
	/// <returns>The filtered query.</returns>
	internal static IQueryable<ArticleRevision> ApplyBucketFilter(IQueryable<ArticleRevision> Query, string? BucketSlug)
	{
		if (string.IsNullOrEmpty(BucketSlug))
		{
			return Query;
		}

		if (BucketSlug == "0-9")
		{
			return Query.Where(r =>
				string.Compare(EF.Functions.Collate(r.Title, "NOCASE"), "0") >= 0
				&& string.Compare(EF.Functions.Collate(r.Title, "NOCASE"), ":") < 0);
		}

		if (BucketSlug == "other")
		{
			return Query.Where(r =>
				string.Compare(EF.Functions.Collate(r.Title, "NOCASE"), "0") < 0
				|| (string.Compare(EF.Functions.Collate(r.Title, "NOCASE"), ":") >= 0
					&& string.Compare(EF.Functions.Collate(r.Title, "NOCASE"), "a") < 0)
				|| string.Compare(EF.Functions.Collate(r.Title, "NOCASE"), "{") >= 0);
		}

		char Letter = char.ToLowerInvariant(BucketSlug[0]);
		string RangeStart = Letter.ToString();
		string RangeEnd = ((char)(Letter + 1)).ToString();

		return Query.Where(r =>
			string.Compare(EF.Functions.Collate(r.Title, "NOCASE"), RangeStart) >= 0
			&& string.Compare(EF.Functions.Collate(r.Title, "NOCASE"), RangeEnd) < 0);
	}

	/// <summary>
	/// Builds links to the site's other language editions for the HTML sitemap hub.
	/// </summary>
	/// <param name="SiteId">The current site ID.</param>
	/// <param name="CurrentCulture">The current culture code.</param>
	/// <param name="Scheme">The request scheme.</param>
	/// <param name="Host">The request host name.</param>
	/// <param name="Port">The optional request port.</param>
	/// <returns>Ordered language links excluding the current culture.</returns>
	private IReadOnlyList<SitemapCultureLink> BuildCultureLinks(int SiteId, string CurrentCulture, string Scheme, string Host, int? Port)
	{
		SiteInfo? Site = SiteConfiguration.Sites.FirstOrDefault(S => S.SiteId == SiteId);
		if (Site is null || Site.Domains.Count == 0)
		{
			return [];
		}

		try
		{
			string RootDomain = SiteCultureHelper.GetRootDomain(Site, Host);
			return
			[
				.. SiteCultureHelper.GetCultures(Site, RootDomain)
					.Where(Code => !Code.Equals(CurrentCulture, StringComparison.OrdinalIgnoreCase))
					.Select(Code => new SitemapCultureLink(
						Code,
						SiteCultureHelper.GetDisplayName(Code),
						$"{SiteCultureHelper.BuildCultureOrigin(Site, RootDomain, Scheme, Port, Code)}/info:sitemap"))
					.OrderBy(Link => Link.DisplayName, StringComparer.OrdinalIgnoreCase)
			];
		}
		catch (InvalidOperationException)
		{
			return [];
		}
	}

	/// <summary>
	/// Gets the scheme and host prefix from the current HTTP context.
	/// </summary>
	/// <returns>The base URL without a trailing slash.</returns>
	/// <exception cref="InvalidOperationException">Thrown when HTTP context is not available.</exception>
	private string GetBaseUrl()
	{
		HttpContext? Context = HttpContextAccessor.HttpContext;
		if (Context is null)
		{
			throw new InvalidOperationException("HTTP context is not available for sitemap generation.");
		}

		HttpRequest Request = Context.Request;
		return $"{Request.Scheme}://{Request.Host}";
	}

	/// <summary>
	/// Writes a UTF-8 XML document string using a low-allocation <see cref="XmlWriter"/>.
	/// </summary>
	/// <param name="WriteBody">Callback emitting the root element and children.</param>
	/// <returns>The serialized XML document.</returns>
	private static string WriteXml(Action<XmlWriter> WriteBody)
	{
		using MemoryStream Buffer = new();
		XmlWriterSettings Settings = new()
		{
			Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			Indent = false,
			OmitXmlDeclaration = false
		};

		using (XmlWriter Writer = XmlWriter.Create(Buffer, Settings))
		{
			Writer.WriteStartDocument();
			WriteBody(Writer);
			Writer.WriteEndDocument();
		}

		return Encoding.UTF8.GetString(Buffer.GetBuffer(), 0, (int)Buffer.Length);
	}

	/// <summary>
	/// Writes a single <c>&lt;sitemap&gt;&lt;loc&gt;...&lt;/loc&gt;&lt;/sitemap&gt;</c> element.
	/// </summary>
	/// <param name="Writer">The XML writer.</param>
	/// <param name="Location">The absolute chunk sitemap URL.</param>
	private static void WriteSitemapLocElement(XmlWriter Writer, string Location)
	{
		Writer.WriteStartElement("sitemap", SitemapNamespace);
		Writer.WriteElementString("loc", SitemapNamespace, Location);
		Writer.WriteEndElement();
	}

	/// <summary>
	/// Writes a <c>&lt;url&gt;</c> element for a static utility path without a <c>lastmod</c> timestamp.
	/// </summary>
	/// <param name="Writer">The XML writer.</param>
	/// <param name="BaseUrl">The base URL.</param>
	/// <param name="Path">The relative path.</param>
	private static void WriteStaticUrlElement(XmlWriter Writer, string BaseUrl, string Path)
	{
		Writer.WriteStartElement("url", SitemapNamespace);
		Writer.WriteElementString("loc", SitemapNamespace, $"{BaseUrl}{Path}");
		Writer.WriteEndElement();
	}

	/// <summary>
	/// Writes a <c>&lt;url&gt;</c> element with <c>loc</c> and ISO-8601 <c>lastmod</c> for an article entry.
	/// </summary>
	/// <param name="Writer">The XML writer.</param>
	/// <param name="BaseUrl">The base URL.</param>
	/// <param name="Entry">The projected article entry.</param>
	private static void WriteArticleUrlElement(XmlWriter Writer, string BaseUrl, XmlArticleProjection Entry)
	{
		Writer.WriteStartElement("url", SitemapNamespace);
		Writer.WriteElementString("loc", SitemapNamespace, $"{BaseUrl}{ArticleUrlHelper.BuildArticlePath(Entry.UrlSlug, Entry.Type)}");
		Writer.WriteElementString("lastmod", SitemapNamespace, Entry.DateCreated.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
		Writer.WriteEndElement();
	}

	/// <summary>
	/// Builds the ordered static bucket list (<c>a</c>–<c>z</c>, <c>0-9</c>, <c>other</c>).
	/// </summary>
	/// <returns>All bucket definitions.</returns>
	private static IReadOnlyList<(string Slug, string Label)> BuildBucketDefinitions()
	{
		List<(string Slug, string Label)> Buckets = new(28);
		for (char Letter = 'a'; Letter <= 'z'; Letter++)
		{
			Buckets.Add((Letter.ToString(), char.ToUpperInvariant(Letter).ToString()));
		}

		Buckets.Add(("0-9", "0–9"));
		Buckets.Add(("other", "Other"));
		return Buckets.AsReadOnly();
	}

	/// <summary>
	/// Builds an empty bucket list with <see cref="SitemapBucketLink.HasEntries"/> set to <see langword="false"/>.
	/// </summary>
	/// <returns>The empty bucket links.</returns>
	private static IReadOnlyList<SitemapBucketLink> BuildEmptyBuckets() =>
		[.. BucketDefinitions.Select(B => new SitemapBucketLink(B.Slug, B.Label, false))];

	/// <summary>
	/// Lightweight SQL projection for XML sitemap entries.
	/// </summary>
	/// <param name="UrlSlug">The article URL slug.</param>
	/// <param name="Type">The article type.</param>
	/// <param name="DateCreated">The creation/last-updated timestamp.</param>
	private sealed record XmlArticleProjection(string UrlSlug, ArticleType Type, DateTimeOffset DateCreated);

	/// <summary>
	/// Lightweight SQL projection for HTML sitemap entries.
	/// </summary>
	/// <param name="UrlSlug">The article URL slug.</param>
	/// <param name="Title">The canonical title, listed as-is; display titles are only for the article header.</param>
	/// <param name="Type">The article type.</param>
	private sealed record HtmlRawProjection(string UrlSlug, string Title, ArticleType Type);

	/// <summary>
	/// Inclusive <c>Id</c> bounds for one XML chunk file.
	/// </summary>
	/// <param name="StartIdInclusive">The first revision ID in the chunk.</param>
	/// <param name="EndIdInclusive">The last revision ID in the chunk.</param>
	private sealed record XmlChunkBoundary(int StartIdInclusive, int EndIdInclusive);
}
