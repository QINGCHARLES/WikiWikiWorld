namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents an entry in the sitemap for an article.
/// </summary>
/// <param name="UrlSlug">The URL slug of the article.</param>
/// <param name="LastUpdated">The date and time when the article was last updated.</param>
public record ArticleSitemapEntry(string UrlSlug, DateTimeOffset LastUpdated);
