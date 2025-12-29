using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve talk subjects created by a specific user (sent messages).
/// </summary>
public sealed class ArticleTalkSubjectsByCreatorSpec : Specification<ArticleTalkSubject>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArticleTalkSubjectsByCreatorSpec"/> class.
    /// </summary>
    /// <param name="SiteId">The site ID.</param>
    /// <param name="CreatedByUserId">The user ID who created the messages.</param>
    public ArticleTalkSubjectsByCreatorSpec(int SiteId, Guid CreatedByUserId)
    {
        Query.AsNoTracking()
             .Where(x => x.SiteId == SiteId && x.CreatedByUserId == CreatedByUserId && x.DateDeleted == null)
             .OrderByDescending(x => x.DateCreated);
    }
}
