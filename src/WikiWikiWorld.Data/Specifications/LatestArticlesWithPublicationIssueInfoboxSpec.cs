using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Retrieves recent articles with a standalone cover carousel or legacy publication issue infobox.
/// </summary>
public sealed class LatestArticlesWithPublicationIssueInfoboxSpec : Specification<ArticleRevision>
{
	/// <summary>
	/// Selects recent cover-bearing articles, including migrated issues whose infobox contained only covers.
	/// </summary>
	/// <param name="Take">The number of articles to retrieve.</param>
	public LatestArticlesWithPublicationIssueInfoboxSpec(int Take)
	{
		Query.AsNoTracking()
			 .Where(x => x.IsCurrent && (x.Text.Contains("{{CoverCarousel") || x.Text.Contains("{{PublicationIssueInfobox")))
			 .OrderByDescending(x => x.DateCreated)
			 .Take(Take);
	}
}
