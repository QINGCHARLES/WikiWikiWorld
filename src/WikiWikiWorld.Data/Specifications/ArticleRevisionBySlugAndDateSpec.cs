using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve a specific article revision by slug and date.
/// </summary>
public sealed class ArticleRevisionBySlugAndDateSpec : Specification<ArticleRevision>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ArticleRevisionBySlugAndDateSpec"/> class.
	/// </summary>
	/// <param name="UrlSlug">The URL slug.</param>
	/// <param name="DateCreated">The creation date of the revision.</param>
	public ArticleRevisionBySlugAndDateSpec(string UrlSlug, DateTimeOffset DateCreated)
	{
		Query.AsNoTracking()
			 .Where(x => x.UrlSlug == UrlSlug && x.DateCreated == DateCreated);
	}
}
