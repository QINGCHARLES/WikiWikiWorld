using WikiWikiWorld.Web.Services;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Supplies the canonical target and persisted state for the bookmark control fragment.
/// </summary>
/// <param name="ArticlePath">The local article URL used for the antiforgery-protected POSTs.</param>
/// <param name="IsSignedIn">
/// Whether the visitor is signed in. Signed-in users get the split button with the list menu; anonymous visitors get the
/// single button, whose POST answers with a sign-in prompt.
/// </param>
/// <param name="Lists">The signed-in user's lists, default first, with whether each contains the article; empty when signed out.</param>
public sealed record BookmarkControlModel(string ArticlePath, bool IsSignedIn, IReadOnlyList<BookmarkListMembership> Lists)
{
	/// <summary>
	/// Gets a value indicating whether the article is in the user's default list, which drives the one-tap button state.
	/// </summary>
	public bool IsBookmarked => Lists.Any(l => l.IsDefault && l.ContainsArticle);

	/// <summary>
	/// Gets how many non-default lists contain the article, shown as a badge on the menu chevron.
	/// </summary>
	public int OtherListCount => Lists.Count(l => !l.IsDefault && l.ContainsArticle);
}
