using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve a user's default bookmark list for the current site.
/// </summary>
/// <remarks>
/// The site is enforced by the DbContext site filter; this spec only narrows by owner + default flag.
/// </remarks>
public sealed class DefaultBookmarkListByUserSpec : Specification<BookmarkList>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="DefaultBookmarkListByUserSpec"/> class.
	/// </summary>
	/// <param name="UserId">The owner's user identifier.</param>
	public DefaultBookmarkListByUserSpec(Guid UserId)
	{
		Query.Where(x => x.CreatedByUserId == UserId && x.IsDefault);
	}
}
