using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve the active bookmark for a given list + canonical article pair.
/// </summary>
/// <remarks>
/// Soft-deleted rows are excluded by the DbContext filter, so this returns the live row used by the toggle handler.
/// </remarks>
public sealed class BookmarkByListAndArticleSpec : Specification<Bookmark>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BookmarkByListAndArticleSpec"/> class.
	/// </summary>
	/// <param name="BookmarkListId">The parent bookmark list identifier.</param>
	/// <param name="CanonicalArticleId">The canonical article identifier.</param>
	public BookmarkByListAndArticleSpec(int BookmarkListId, Guid CanonicalArticleId)
	{
		Query.Where(x => x.BookmarkListId == BookmarkListId && x.CanonicalArticleId == CanonicalArticleId);
	}
}
