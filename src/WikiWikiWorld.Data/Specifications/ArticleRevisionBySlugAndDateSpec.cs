using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve a specific article revision by slug and creation date.
/// </summary>
public sealed class ArticleRevisionBySlugAndDateSpec : Specification<ArticleRevision>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArticleRevisionBySlugAndDateSpec"/> class.
    /// </summary>
    /// <param name="SiteId">The site ID.</param>
    /// <param name="Culture">The culture code.</param>
    /// <param name="UrlSlug">The URL slug.</param>
    /// <param name="DateCreated">The creation date of the revision.</param>
    public ArticleRevisionBySlugAndDateSpec(int SiteId, string Culture, string UrlSlug, DateTimeOffset DateCreated)
    {
        Query.AsNoTracking()
             .Where(x => x.SiteId == SiteId && x.Culture == Culture && x.UrlSlug == UrlSlug && x.DateCreated == DateCreated);
    }
}
