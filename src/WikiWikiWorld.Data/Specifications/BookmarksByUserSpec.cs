using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve all active bookmarks owned by a user, newest first.
/// </summary>
public sealed class BookmarksByUserSpec : Specification<Bookmark>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BookmarksByUserSpec"/> class.
	/// </summary>
	/// <param name="UserId">The owner's user identifier.</param>
	public BookmarksByUserSpec(Guid UserId)
	{
		Query.AsNoTracking()
			 .Where(x => x.CreatedByUserId == UserId)
			 .OrderByDescending(x => x.DateCreated);
	}
}
