namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Describes one of the user's bookmark lists and whether a particular article is saved in it.
/// </summary>
/// <param name="BookmarkListId">The list identifier, used as the toggle target for non-default lists.</param>
/// <param name="Name">The list's display name.</param>
/// <param name="IsDefault">Whether this is the user's default list, which the one-tap bookmark button toggles.</param>
/// <param name="ContainsArticle">Whether the article currently has an active bookmark in this list.</param>
public sealed record BookmarkListMembership(int BookmarkListId, string Name, bool IsDefault, bool ContainsArticle);
