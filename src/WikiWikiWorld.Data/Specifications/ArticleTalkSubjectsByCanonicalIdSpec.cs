using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve talk subjects by canonical article identifier.
/// </summary>
public sealed class ArticleTalkSubjectsByCanonicalIdSpec : Specification<ArticleTalkSubject>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArticleTalkSubjectsByCanonicalIdSpec"/> class.
    /// </summary>
    /// <param name="SiteId">The site ID.</param>
    /// <param name="CanonicalArticleId">The canonical article identifier.</param>
    public ArticleTalkSubjectsByCanonicalIdSpec(int SiteId, Guid CanonicalArticleId)
    {
        Query.AsNoTracking()
             .Where(x => x.SiteId == SiteId && x.CanonicalArticleId == CanonicalArticleId && x.DateDeleted == null)
             .OrderByDescending(x => x.DateCreated);
    }
}
