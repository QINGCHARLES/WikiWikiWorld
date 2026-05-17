using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve a single <see cref="ArticleTalkSubject"/> by its parent article and URL slug.
/// </summary>
/// <remarks>
/// Scoping the lookup to <see cref="ArticleTalkSubject.CanonicalArticleId"/> + <see cref="ArticleTalkSubject.UrlSlug"/>
/// prevents misrouting when two different articles share a subject slug.
/// </remarks>
public sealed class ArticleTalkSubjectBySlugSpec : Specification<ArticleTalkSubject>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ArticleTalkSubjectBySlugSpec"/> class.
	/// </summary>
	/// <param name="CanonicalArticleId">The canonical article identifier the subject belongs to.</param>
	/// <param name="UrlSlug">The URL slug of the talk subject.</param>
	public ArticleTalkSubjectBySlugSpec(Guid CanonicalArticleId, string UrlSlug)
	{
		Query.Where(x => x.CanonicalArticleId == CanonicalArticleId
		              && x.UrlSlug == UrlSlug
		              && x.DateDeleted == null);
	}
}
