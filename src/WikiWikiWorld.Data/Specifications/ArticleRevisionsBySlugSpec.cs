using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve article revisions by URL slug.
/// </summary>
public sealed class ArticleRevisionsBySlugSpec : Specification<ArticleRevision>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ArticleRevisionsBySlugSpec"/> class.
	/// </summary>
	/// <param name="UrlSlug">The URL slug.</param>
	/// <param name="IsCurrent">Whether to filter by current revision. Default is true.</param>
	public ArticleRevisionsBySlugSpec(string UrlSlug, bool? IsCurrent = true)
	{
		Query.AsNoTracking()
			 .Where(x => x.UrlSlug == UrlSlug);

		if (IsCurrent.HasValue)
		{
			Query.Where(x => x.IsCurrent == IsCurrent.Value);
		}
		Query.OrderByDescending(x => x.DateCreated);
	}
}
