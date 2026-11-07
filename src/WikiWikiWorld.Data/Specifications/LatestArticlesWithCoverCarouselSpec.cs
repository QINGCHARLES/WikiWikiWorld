using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Retrieves recent standard articles that contain a <c>{{CoverCarousel}}</c> directive.
/// </summary>
public sealed class LatestArticlesWithCoverCarouselSpec : Specification<ArticleRevision>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LatestArticlesWithCoverCarouselSpec"/> class.
	/// </summary>
	/// <param name="Take">The number of articles to retrieve.</param>
	public LatestArticlesWithCoverCarouselSpec(int Take)
	{
		Query.AsNoTracking()
			.Where(x => x.IsCurrent && x.DateDeleted == null && x.Type == ArticleType.Article && x.Text.Contains("{{CoverCarousel"))
			.OrderByDescending(x => x.DateCreated)
			.Take(Take);
	}
}
