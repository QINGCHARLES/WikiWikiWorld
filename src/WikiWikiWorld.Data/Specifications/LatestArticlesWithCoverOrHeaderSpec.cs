using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Retrieves recent standard articles for a site and culture that contain a <c>{{CoverCarousel}}</c>
/// or <c>{{HeaderImage}}</c> directive.
/// </summary>
public sealed class LatestArticlesWithCoverOrHeaderSpec : Specification<ArticleRevision>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LatestArticlesWithCoverOrHeaderSpec"/> class.
	/// </summary>
	/// <param name="SiteId">The site identifier to filter by.</param>
	/// <param name="Culture">The culture code to filter by.</param>
	/// <param name="Take">The maximum number of candidate articles to retrieve.</param>
	public LatestArticlesWithCoverOrHeaderSpec(int SiteId, string Culture, int Take)
	{
		Query.AsNoTracking()
			.Where(x => x.SiteId == SiteId
				&& x.Culture == Culture
				&& x.IsCurrent
				&& x.DateDeleted == null
				&& x.Type == ArticleType.Article
				&& (x.Text.Contains("{{CoverCarousel") || x.Text.Contains("{{HeaderImage ")))
			.OrderByDescending(x => x.DateCreated)
			.ThenByDescending(x => x.Id)
			.Take(Take);
	}
}
