using Ardalis.Specification;

using Ardalis.Specification;

using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve article revisions by URL slug.
/// </summary>
public sealed class ArticleRevisionsBySlugSpec : Specification<ArticleRevision>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArticleRevisionsBySlugSpec"/> class.
    /// </summary>
    /// <param name="SiteId">The site ID.</param>
    /// <param name="Culture">The culture code.</param>
    /// <param name="UrlSlug">The URL slug.</param>
    /// <param name="IsCurrent">Whether to filter by current revision. Default is true.</param>
    public ArticleRevisionsBySlugSpec(int SiteId, string Culture, string UrlSlug, bool? IsCurrent = true)
    {
        Query.AsNoTracking()
             .Where(x => x.SiteId == SiteId && x.Culture == Culture && x.UrlSlug == UrlSlug);

        if (IsCurrent.HasValue)
        {
            Query.Where(x => x.IsCurrent == IsCurrent.Value);
        }
        Query.OrderByDescending(x => x.DateCreated);
    }
}
