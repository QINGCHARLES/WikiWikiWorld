using Ardalis.Specification;
using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve article revisions by canonical article ID.
/// </summary>
public sealed class ArticleRevisionsByCanonicalIdSpec : Specification<ArticleRevision>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArticleRevisionsByCanonicalIdSpec"/> class.
    /// </summary>
    /// <param name="CanonicalArticleId">The canonical article ID.</param>
    /// <param name="MaxDate">Optional maximum date to filter revisions.</param>
    public ArticleRevisionsByCanonicalIdSpec(Guid CanonicalArticleId, DateTimeOffset? MaxDate)
    {
        Query.Where(x => x.CanonicalArticleId == CanonicalArticleId);

        if (MaxDate.HasValue)
        {
            Query.Where(x => x.DateCreated <= MaxDate.Value);
        }
        Query.OrderByDescending(x => x.DateCreated);
    }
}
