namespace WikiWikiWorld.Web.Services;

/// <summary>A list visible to the viewer, with a count of current articles in this site and culture.</summary>
/// <param name="Id">Stable list identity; zero represents an owner's not-yet-created default list.</param>
/// <param name="Name">Plain-text display name.</param>
/// <param name="IsDefault">Whether the article's main bookmark action saves here.</param>
/// <param name="IsPublic">Whether other readers can see the list.</param>
/// <param name="Count">Visible active article memberships.</param>
public sealed record class BookmarkListSummary(int Id, string Name, bool IsDefault, bool IsPublic, int Count);

/// <summary>A projected bookmark row without loading article bodies.</summary>
/// <param name="Id">Membership identity, used for removal.</param>
/// <param name="Title">Current article title.</param>
/// <param name="UrlSlug">Current article slug.</param>
/// <param name="Type">Article namespace used to form the link.</param>
/// <param name="DateCreated">Original UTC save instant.</param>
public sealed record class BookmarkEntry(int Id, string Title, string UrlSlug, ArticleType Type, DateTimeOffset DateCreated);

/// <summary>A bounded, deterministically ordered page of visible memberships.</summary>
/// <param name="Items">At most 50 entries.</param>
/// <param name="TotalCount">Number of visible entries before paging.</param>
/// <param name="PageNumber">Requested page clamped to the available range.</param>
public sealed record class BookmarkEntryPage(IReadOnlyList<BookmarkEntry> Items, int TotalCount, int PageNumber)
{
	/// <summary>Gets the number of pages, including the single empty page of an empty list.</summary>
	public int PageCount => Math.Max(1, (TotalCount + BookmarkService.PageSize - 1) / BookmarkService.PageSize);
}

/// <summary>The outcome of creating an empty list without silently reusing a duplicate name.</summary>
/// <param name="ListId">Created identity, or null when validation failed.</param>
/// <param name="Error">Actionable validation message, or null on success.</param>
public sealed record class BookmarkListCreation(int? ListId, string? Error);
