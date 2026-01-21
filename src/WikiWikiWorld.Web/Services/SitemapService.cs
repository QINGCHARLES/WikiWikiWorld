using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Interface for the sitemap service.
/// </summary>
public interface ISitemapService
{
    /// <summary>
    /// Generates the sitemap XML.
    /// </summary>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>The sitemap XML as a string.</returns>
    Task<string> GenerateSitemapAsync(CancellationToken CancellationToken = default);
}

/// <summary>
/// Service to generate the sitemap.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolver">The site resolver service.</param>
/// <param name="MemoryCache">The memory cache.</param>
/// <param name="HttpContextAccessor">The HTTP context accessor.</param>
public sealed class SitemapService(
    WikiWikiWorldDbContext Context,
    SiteResolverService SiteResolver,
    IMemoryCache MemoryCache,
    IHttpContextAccessor HttpContextAccessor) : ISitemapService
{
    // Cache keys
    private const string SitemapCacheKeyPrefix = "Sitemap_";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6); // Cache for 6 hours

    /// <inheritdoc/>
    public async Task<string> GenerateSitemapAsync(CancellationToken CancellationToken = default)
    {
        string BaseUrl = GetBaseUrl();

        try
        {
            // Get the current site context using the resolver
            (int SiteId, string Culture, bool IsCultureSelectorRootDomain) = SiteResolver.ResolveSiteAndCultureWithRootCheck();

            // If this is a culture-selector root domain (e.g., magazedia.com with RootDomainIsCultureSelectorOnly=true),
            // return minimal sitemap - the full sitemap is served from culture subdomains (e.g., en.magazedia.com)
            if (IsCultureSelectorRootDomain)
            {
                return GenerateRootSitemapXml(BaseUrl);
            }

            // Create a site-specific cache key that includes the host
            string CacheKey = $"{SitemapCacheKeyPrefix}{SiteId}_{Culture}_{BaseUrl}";

            // Try to get the sitemap from the cache
            if (MemoryCache.TryGetValue(CacheKey, out string? CachedSitemap) && CachedSitemap is not null)
            {
                return CachedSitemap;
            }

            // If not cached, generate the sitemap
            string GeneratedSitemap = await GenerateSitemapXmlAsync(SiteId, Culture, BaseUrl, CancellationToken);

            // Cache the result with an absolute expiration
            MemoryCacheEntryOptions CacheOptions = new()
            {
                AbsoluteExpirationRelativeToNow = CacheDuration,
                Priority = CacheItemPriority.Normal
            };

            MemoryCache.Set(CacheKey, GeneratedSitemap, CacheOptions);

            return GeneratedSitemap;
        }
        catch (InvalidOperationException)
        {
            // Root domain (no culture) - return minimal sitemap with just the homepage
            return GenerateRootSitemapXml(BaseUrl);
        }
    }

    /// <summary>
    /// Generates minimal sitemap XML for culture-selector root domains.
    /// </summary>
    /// <param name="BaseUrl">The base URL for the sitemap.</param>
    /// <returns>The sitemap XML as a string.</returns>
    private static string GenerateRootSitemapXml(string BaseUrl)
    {
        XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        XDocument Doc = new(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(Ns + "urlset",
                new XElement(Ns + "url",
                    new XElement(Ns + "loc", $"{BaseUrl}/")
                )
            )
        );

        return Doc.ToString();
    }

    /// <summary>
    /// Generates the full sitemap XML with all current articles.
    /// </summary>
    /// <param name="SiteId">The site ID.</param>
    /// <param name="Culture">The culture code.</param>
    /// <param name="BaseUrl">The base URL for the sitemap.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>The sitemap XML as a string.</returns>
    private async Task<string> GenerateSitemapXmlAsync(int SiteId, string Culture, string BaseUrl, CancellationToken CancellationToken)
    {
        // Get all current articles for this site and culture
        IReadOnlyList<ArticleRevision> Articles = await Context.ArticleRevisions
            .AsNoTracking()
            .Where(x => x.SiteId == SiteId && x.Culture == Culture && x.IsCurrent)
            .ToListAsync(CancellationToken);

        // Map to ArticleSitemapEntry
        var Entries = Articles.Select(a => new ArticleSitemapEntry(a.UrlSlug, a.DateCreated));

        // Generate XML elements for each article using collection expressions
        XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XElement[] UrlElements = [
            .. Entries.Select(Article =>
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

    /// <summary>
    /// Gets the base URL from the current HTTP context.
    /// </summary>
    /// <returns>The base URL (scheme and host).</returns>
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
}