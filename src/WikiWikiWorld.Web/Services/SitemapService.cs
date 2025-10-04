using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;

namespace WikiWikiWorld.Web.Services;

public interface ISitemapService
{
	Task<string> GenerateSitemapAsync();
}

public sealed class SitemapService(
	IArticleRevisionRepository ArticleRepository,
	SiteResolverService SiteResolver,
	IMemoryCache MemoryCache,
	IHttpContextAccessor HttpContextAccessor) : ISitemapService
{
	// Cache keys
	private const string SitemapCacheKeyPrefix = "Sitemap_";
	private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6); // Cache for 6 hours
	
	public async Task<string> GenerateSitemapAsync()
	{
		// Get the current site context using the resolver
		(int SiteId, string Culture) = SiteResolver.ResolveSiteAndCulture();
		
		// Create a site-specific cache key
		string CacheKey = $"{SitemapCacheKeyPrefix}{SiteId}_{Culture}";
		
		// Try to get the sitemap from the cache
		if (MemoryCache.TryGetValue(CacheKey, out string? CachedSitemap) && CachedSitemap is not null)
		{
			return CachedSitemap;
		}
		
		// If not cached, generate the sitemap
		string BaseUrl = GetBaseUrl();
		string GeneratedSitemap = await GenerateSitemapXmlAsync(SiteId, Culture, BaseUrl);

		// Cache the result with an absolute expiration
		MemoryCacheEntryOptions CacheOptions = new()
		{
			AbsoluteExpirationRelativeToNow = CacheDuration,
			Priority = CacheItemPriority.Normal
		};

		MemoryCache.Set(CacheKey, GeneratedSitemap, CacheOptions);
		
		return GeneratedSitemap;
	}
	
	private async Task<string> GenerateSitemapXmlAsync(int SiteId, string Culture, string BaseUrl)
	{
		// Get all current articles for this site and culture
		IReadOnlyList<ArticleSitemapEntry> Articles =
			await ArticleRepository.GetAllCurrentArticlesForSitemapAsync(
				SiteId,
				Culture);

		// Generate XML elements for each article using collection expressions
		XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
		XElement[] UrlElements = [
			.. Articles.Select(Article =>
				new XElement(Ns + "url",
					new XElement(Ns + "loc", $"{BaseUrl}/{Article.UrlSlug}"),
					new XElement(Ns + "lastmod", Article.LastUpdated.ToString("yyyy-MM-ddTHH:mm:sszzz"))
				)
			)
		];

		// Create the sitemap document with target-typed new
		XDocument Doc = new(
			new XDeclaration("1.0", "utf-8", null),
			new XElement(Ns + "urlset", UrlElements)
		);

		return Doc.ToString();
	}

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
}