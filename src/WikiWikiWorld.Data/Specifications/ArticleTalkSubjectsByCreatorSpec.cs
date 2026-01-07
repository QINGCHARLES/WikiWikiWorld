using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve talk subjects created by a specific user (sent messages).
/// </summary>
public sealed class ArticleTalkSubjectsByCreatorSpec : Specification<ArticleTalkSubject>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ArticleTalkSubjectsByCreatorSpec"/> class.
	/// </summary>
	/// <param name="CreatedByUserId">The user ID who created the messages.</param>
	public ArticleTalkSubjectsByCreatorSpec(Guid CreatedByUserId)
	{
		Query.AsNoTracking()
			 .Where(x => x.CreatedByUserId == CreatedByUserId)
			 .OrderByDescending(x => x.DateCreated);
	}
}
