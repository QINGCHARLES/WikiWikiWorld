namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a single bookmarked article within a <see cref="BookmarkList"/>.
/// </summary>
public sealed record Bookmark
{
	/// <summary>
	/// Gets the unique identifier for this bookmark.
	/// </summary>
	public int Id { get; init; }

	/// <summary>
	/// Gets the identifier of the bookmark list this bookmark belongs to.
	/// </summary>
	public int BookmarkListId { get; init; }

	/// <summary>
	/// Gets the site identifier.
	/// </summary>
	/// <remarks>
	/// Denormalized from the parent list so the "is this article bookmarked by this user?" lookup
	/// on every article page is a single indexed read.
	/// </remarks>
	public int SiteId { get; init; }

	/// <summary>
	/// Gets the canonical identifier of the bookmarked article.
	/// </summary>
	public Guid CanonicalArticleId { get; init; }

	/// <summary>
	/// Gets the user identifier who created this bookmark.
	/// </summary>
	public Guid CreatedByUserId { get; init; }

	/// <summary>
	/// Gets the date and time when this bookmark was created.
	/// </summary>
	public DateTimeOffset DateCreated { get; init; }

	/// <summary>
	/// Gets the date and time when this bookmark was deleted, if applicable.
	/// </summary>
	public DateTimeOffset? DateDeleted { get; set; }
}
