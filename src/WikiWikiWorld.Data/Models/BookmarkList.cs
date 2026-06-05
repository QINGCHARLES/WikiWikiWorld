namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a named collection of article bookmarks owned by a user.
/// </summary>
/// <remarks>
/// Each user has a single list flagged as <see cref="IsDefault"/>, created lazily on first bookmark.
/// Additional lists are reserved for a future multi-list feature.
/// </remarks>
public sealed record BookmarkList
{
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

	/// <summary>
	/// Gets a value indicating whether this is the user's default list.
	/// </summary>
	public bool IsDefault { get; init; }

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
