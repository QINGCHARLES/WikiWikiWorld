namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a named collection of article bookmarks owned by a user.
/// </summary>
/// <remarks>
/// Each user has a single list flagged as <see cref="IsDefault"/>, created lazily on first bookmark.
/// Lists are private until their owner explicitly makes them public.
/// </remarks>
public sealed record BookmarkList
{
	/// <summary>The maximum number of UTF-16 code units accepted in a list's Markdown introduction.</summary>
	public const int MaxDescriptionLength = 10_000;

	/// <summary>
	/// Gets the unique identifier for this list.
	/// </summary>
	public int Id { get; init; }

	/// <summary>
	/// Gets the site identifier.
	/// </summary>
	public int SiteId { get; init; }

	/// <summary>
	/// Gets the display name of this list.
	/// </summary>
	public required string Name { get; init; }

	/// <summary>Gets or sets the optional Markdown introduction shared by every language view of this list.</summary>
	/// <remarks>Stores source rather than HTML; visibility follows the list. Service writes normalize blank input to null.</remarks>
	public string? Description { get; set; }

	/// <summary>
	/// Gets a value indicating whether this is the user's default list.
	/// </summary>
	public bool IsDefault { get; init; }

	/// <summary>Gets or sets whether anyone may read this list; existing and new lists default to private.</summary>
	public bool IsPublic { get; set; }

	/// <summary>
	/// Gets the user identifier who created (and owns) this list.
	/// </summary>
	public Guid CreatedByUserId { get; init; }

	/// <summary>
	/// Gets the date and time when this list was created.
	/// </summary>
	public DateTimeOffset DateCreated { get; init; }

	/// <summary>
	/// Gets the date and time when this list was last modified, if applicable.
	/// </summary>
	public DateTimeOffset? DateModified { get; init; }

	/// <summary>
	/// Gets the date and time when this list was deleted, if applicable.
	/// </summary>
	public DateTimeOffset? DateDeleted { get; set; }

	/// <summary>
	/// Gets the bookmarks contained in this list.
	/// </summary>
	public ICollection<Bookmark> Bookmarks { get; init; } = [];
}
