using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve an ArticleTalkSubject by its URL slug.
/// </summary>
public sealed class ArticleTalkSubjectBySlugSpec : Specification<ArticleTalkSubject>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArticleTalkSubjectBySlugSpec"/> class.
    /// </summary>
    /// <param name="SiteId">The site ID.</param>
    /// <param name="UrlSlug">The URL slug of the talk subject.</param>
    public ArticleTalkSubjectBySlugSpec(int SiteId, string UrlSlug)
    {
        // TODO: Consider adding pagination for ArticleTalkSubjectPosts in discussions with many replies.
        // For large threads, loading all posts at once may impact performance.
        Query.Where(x => x.SiteId == SiteId && x.UrlSlug == UrlSlug)
             .Include(x => x.ArticleTalkSubjectPosts);
    }
}
