using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

public sealed class ArticleRevisionsByUserIdSpec : Specification<ArticleRevision>
{
    public ArticleRevisionsByUserIdSpec(Guid UserId)
    {
        Query.Where(Revision => Revision.CreatedByUserId == UserId)
             .OrderByDescending(Revision => Revision.DateCreated);
    }
}
