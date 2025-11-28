using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve the latest articles that contain a publication issue infobox.
/// </summary>
public sealed class LatestArticlesWithPublicationIssueInfoboxSpec : Specification<ArticleRevision>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LatestArticlesWithPublicationIssueInfoboxSpec"/> class.
    /// </summary>
    /// <param name="SiteId">The site ID.</param>
    /// <param name="Culture">The culture code.</param>
    /// <param name="Take">The number of articles to retrieve.</param>
    public LatestArticlesWithPublicationIssueInfoboxSpec(int SiteId, string Culture, int Take)
    {
        Query.AsNoTracking()
             .Where(x => x.SiteId == SiteId && x.Culture == Culture && x.IsCurrent && x.Text.Contains("{{PublicationIssueInfobox"))
             .OrderByDescending(x => x.DateCreated)
             .Take(Take);
    }
}
