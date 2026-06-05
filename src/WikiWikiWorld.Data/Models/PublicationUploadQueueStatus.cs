namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a state in the publication ingestion workflow (SUBMITTED, DOWNLOADED, BEINGCHECKED, REJECTED, ACCEPTED, POSTED).
/// </summary>
public sealed record PublicationUploadQueueStatus
{
	/// <summary>
	/// Gets the unique identifier for this status.
	/// </summary>
	public long Id { get; init; }

	/// <summary>
	/// Gets the stable code. Display strings live in the UI resource files.
	/// </summary>
	public required string Code { get; init; }
}
