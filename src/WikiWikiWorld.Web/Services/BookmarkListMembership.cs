namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Describes one of the user's bookmark lists and, when a particular article is saved in it, that membership and its note.
/// </summary>
/// <param name="BookmarkListId">The list identifier, used as the toggle target for non-default lists.</param>
/// <param name="Name">The list's display name.</param>
/// <param name="IsDefault">Whether this is the user's default list, which the one-tap bookmark button toggles.</param>
/// <param name="IsPublic">Whether other readers can see the list, and so any note written on its entries.</param>
/// <param name="BookmarkId">The active membership of the article in this list, or null when the article is not saved here.</param>
/// <param name="Notes">The membership's Markdown note source, or null when the article is not saved here or has no note.</param>
public sealed record BookmarkListMembership(int BookmarkListId, string Name, bool IsDefault, bool IsPublic = false, int? BookmarkId = null, string? Notes = null)
{
	/// <summary>Gets a value indicating whether the article currently has an active bookmark in this list.</summary>
	public bool ContainsArticle => BookmarkId is not null;
}
