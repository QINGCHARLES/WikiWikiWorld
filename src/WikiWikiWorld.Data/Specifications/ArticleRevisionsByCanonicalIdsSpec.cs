using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve article revisions by a list of canonical article IDs.
/// </summary>
public sealed class ArticleRevisionsByCanonicalIdsSpec : Specification<ArticleRevision>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArticleRevisionsByCanonicalIdsSpec"/> class.
    /// </summary>
    /// <param name="CanonicalArticleIds">The list of canonical article IDs.</param>

    public ArticleRevisionsByCanonicalIdsSpec(IEnumerable<Guid> CanonicalArticleIds)
    {
        Query.Where(x => CanonicalArticleIds.Contains(x.CanonicalArticleId));
        Query.OrderByDescending(x => x.DateCreated);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArticleRevisionsByCanonicalIdsSpec"/> class.
    /// </summary>
    /// <param name="SiteId">The site identifier.</param>
    /// <param name="Culture">The culture (unused but kept for API consistency).</param>
    /// <param name="CanonicalArticleIds">The list of canonical article IDs.</param>
    /// <param name="IsCurrent">Whether to filter by current revision.</param>
    public ArticleRevisionsByCanonicalIdsSpec(int SiteId, string Culture, IEnumerable<Guid> CanonicalArticleIds, bool IsCurrent)
    {
        Query.Where(x => x.SiteId == SiteId && CanonicalArticleIds.Contains(x.CanonicalArticleId));

        if (IsCurrent)
        {
            Query.Where(x => x.IsCurrent);
        }

        Query.OrderByDescending(x => x.DateCreated);
    }
}
