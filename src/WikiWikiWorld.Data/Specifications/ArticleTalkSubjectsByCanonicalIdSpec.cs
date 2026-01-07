using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve talk subjects by canonical article identifier.
/// </summary>
public sealed class ArticleTalkSubjectsByCanonicalIdSpec : Specification<ArticleTalkSubject>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ArticleTalkSubjectsByCanonicalIdSpec"/> class.
	/// </summary>
	/// <param name="CanonicalArticleId">The canonical article identifier.</param>
	public ArticleTalkSubjectsByCanonicalIdSpec(Guid CanonicalArticleId)
	{
		Query.AsNoTracking()
			 .Where(x => x.CanonicalArticleId == CanonicalArticleId)
			 .OrderByDescending(x => x.DateCreated);
	}
}
