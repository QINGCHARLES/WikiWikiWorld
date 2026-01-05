using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve article revisions authored by a specific user.
/// </summary>
public sealed class ArticleRevisionsByUserIdSpec : Specification<ArticleRevision>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArticleRevisionsByUserIdSpec"/> class.
    /// </summary>
    /// <param name="UserId">The ID of the user to retrieve revisions for.</param>
    public ArticleRevisionsByUserIdSpec(Guid UserId)
    {
        Query.Where(Revision => Revision.CreatedByUserId == UserId)
             .OrderByDescending(Revision => Revision.DateCreated);
    }
}
