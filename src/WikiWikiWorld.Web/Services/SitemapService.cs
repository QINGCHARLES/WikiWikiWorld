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