namespace WikiWikiWorld.Web.Services;

/// <summary>
/// The outcome of <see cref="BookmarkService.CreateListWithBookmarkAsync"/>.
/// </summary>
public enum BookmarkListCreateResult
{
	/// <summary>A new list was created holding the article.</summary>
	Created,

	/// <summary>The user already had a list with that name (ignoring case), so the article was saved there instead.</summary>
	AddedToExisting,

	/// <summary>The user already has <see cref="BookmarkService.MaxListsPerUser"/> lists; nothing changed.</summary>
	LimitReached,
}
