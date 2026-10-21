namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Supplies the canonical target and persisted state for the bookmark control fragment.
/// </summary>
/// <param name="ArticlePath">The local article URL used for the antiforgery-protected POST.</param>
/// <param name="IsBookmarked">Whether the current user has saved the article.</param>
public sealed record BookmarkControlModel(string ArticlePath, bool IsBookmarked);
