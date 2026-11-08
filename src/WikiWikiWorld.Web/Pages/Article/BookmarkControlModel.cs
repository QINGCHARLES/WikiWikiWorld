using System.Text.RegularExpressions;
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
/// <param name="Lists">The signed-in user's lists, default first, with the article's membership in each; empty when signed out.</param>
/// <param name="BookmarksPath">The signed-in user's bookmarks page, linked from the menu as "Edit lists"; empty when signed out.</param>
/// <param name="StatusMessage">A confirmation shown in the status line, such as a saved note; empty clears any earlier message.</param>
/// <param name="SavedListId">
/// The list the article was just added to, which makes the response offer "Add a note" (or say a restored note is back);
/// null when the request did not add the article to a list.
/// </param>
/// <param name="ArticleTitle">The article's display title, named in the note dialog; empty in Datastar state responses, which never render it.</param>
public sealed partial record BookmarkControlModel(string ArticlePath, bool IsSignedIn, IReadOnlyList<BookmarkListMembership> Lists, string BookmarksPath = "",
	string StatusMessage = "", int? SavedListId = null, string ArticleTitle = "")
{
	/// <summary>
	/// Builds the bookmarks page path for a user, matching the <c>@{Username}/bookmarks</c> page route.
	/// </summary>
	/// <param name="UserName">The user's name; <see langword="null"/> or blank yields an empty path.</param>
	/// <returns>The local URL of the user's bookmarks page, or an empty string.</returns>
	public static string BuildBookmarksPath(string? UserName) =>
		string.IsNullOrWhiteSpace(UserName) ? string.Empty : $"/@{Uri.EscapeDataString(UserName)}/bookmarks";

	/// <summary>
	/// Gets a value indicating whether the article is in the user's default list, which drives the one-tap button state.
	/// </summary>
	public bool IsBookmarked => Lists.Any(l => l.IsDefault && l.ContainsArticle);

	/// <summary>
	/// Gets how many non-default lists contain the article, shown as a badge on the menu chevron.
	/// </summary>
	public int OtherListCount => Lists.Count(l => !l.IsDefault && l.ContainsArticle);

	/// <summary>
	/// Gets the membership just created by this request, when it still holds the article, for the post-save prompt.
	/// </summary>
	public BookmarkListMembership? SavedMembership => SavedListId is { } Id ? Lists.FirstOrDefault(l => l.BookmarkListId == Id && l.ContainsArticle) : null;

	/// <summary>
	/// Reduces a Markdown note to one line of plain text for the menu: link targets, emphasis and heading markers are
	/// dropped and whitespace collapsed. The menu truncates it visually with an ellipsis.
	/// </summary>
	/// <param name="Notes">The note's Markdown source; null yields an empty string.</param>
	/// <returns>Plain text of at most 160 characters, encoded by Razor when rendered.</returns>
	public static string NoteExcerpt(string? Notes)
	{
		if (string.IsNullOrWhiteSpace(Notes))
			return string.Empty;
		string Text = MarkdownLinkRegex().Replace(Notes, "$1");
		Text = MarkdownMarkerRegex().Replace(Text, string.Empty);
		Text = WhitespaceRegex().Replace(Text, " ").Trim();
		return Text.Length <= 160 ? Text : Text[..160];
	}

	/// <summary>Matches an inline Markdown link or image, capturing its label.</summary>
	/// <returns>The compiled expression.</returns>
	[GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")]
	private static partial Regex MarkdownLinkRegex();

	/// <summary>Matches Markdown emphasis, heading, quote and code markers.</summary>
	/// <returns>The compiled expression.</returns>
	[GeneratedRegex(@"[*_#>`~]")]
	private static partial Regex MarkdownMarkerRegex();

	/// <summary>Matches runs of whitespace, including line breaks.</summary>
	/// <returns>The compiled expression.</returns>
	[GeneratedRegex(@"\s+")]
	private static partial Regex WhitespaceRegex();
}
