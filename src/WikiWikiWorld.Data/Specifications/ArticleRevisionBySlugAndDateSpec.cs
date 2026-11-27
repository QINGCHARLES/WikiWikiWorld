using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve a specific article revision by slug and the span of time a revision timestamp names.
/// </summary>
public sealed class ArticleRevisionBySlugAndDateSpec : Specification<ArticleRevision>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ArticleRevisionBySlugAndDateSpec"/> class, matching the earliest
	/// revision at the slug created at or after <paramref name="From"/> and before <paramref name="Until"/>.
	/// </summary>
	/// <remarks>
	/// Revision URLs and API timestamps often carry less precision than the stored creation time, so an exact
	/// equality match would miss them. On SQLite the comparison runs on the stored UTC text, which sorts in time order
	/// because every value is written with the same <c>+00:00</c> offset.
	/// </remarks>
	/// <param name="UrlSlug">The URL slug.</param>
	/// <param name="From">The first instant the requested timestamp covers (inclusive).</param>
	/// <param name="Until">The first instant after the requested timestamp (exclusive).</param>
	public ArticleRevisionBySlugAndDateSpec(string UrlSlug, DateTimeOffset From, DateTimeOffset Until)
	{
		Query.AsNoTracking()
			 .Where(x => x.UrlSlug == UrlSlug && x.DateCreated >= From && x.DateCreated < Until)
			 .OrderBy(x => x.DateCreated)
			 .ThenBy(x => x.Id);
	}
}
