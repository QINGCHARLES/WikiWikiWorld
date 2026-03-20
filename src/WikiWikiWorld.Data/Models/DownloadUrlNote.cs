namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a user-authored note on a download URL.
/// </summary>
public sealed record DownloadUrlNote
{
	/// <summary>
	/// Gets the unique identifier for this note.
	/// </summary>
	public long Id { get; init; }

	/// <summary>
	/// Gets the identifier of the download URL this note belongs to.
	/// </summary>
	public long DownloadUrlId { get; init; }

	/// <summary>
	/// Gets the user identifier who authored this note.
	/// </summary>
	public Guid UserId { get; init; }

	/// <summary>
	/// Gets the culture code for this note (e.g., "en", "ja").
	/// </summary>
	public required string Culture { get; init; }

	/// <summary>
	/// Gets the text content of the note.
	/// </summary>
	public required string Text { get; init; }

	/// <summary>
	/// Gets the date and time when this note was created.
	/// </summary>
	public DateTimeOffset DateCreated { get; init; }
}
