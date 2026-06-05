namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a single user submission of a remote publication download URL awaiting moderator ingestion.
/// </summary>
public sealed record PublicationUploadQueue
{
	/// <summary>
	/// Gets the unique identifier for this submission row.
	/// </summary>
	public long Id { get; init; }

	/// <summary>
	/// Gets the site identifier.
	/// </summary>
	public int SiteId { get; init; }

	/// <summary>
	/// Gets the culture the submission was made under.
	/// </summary>
	public required string Culture { get; init; }

	/// <summary>
	/// Gets the canonical article identifier the user submitted from, if any.
	/// </summary>
	public Guid? CanonicalArticleId { get; init; }

	/// <summary>
	/// Gets the remote download URL the submitter is offering.
	/// </summary>
	public required string DownloadUrl { get; init; }

	/// <summary>
	/// Gets the optional passphrase the transfer service requires to access the file.
	/// </summary>
	public string? SitePassword { get; init; }

	/// <summary>
	/// Gets the optional passphrase required to open the archive itself (zip/rar/7z).
	/// </summary>
	public string? ArchivePassword { get; init; }

	/// <summary>
	/// Gets the copyright-exemption category selected by the submitter.
	/// </summary>
	public long CopyrightExemptionId { get; init; }

	/// <summary>
	/// Gets the current workflow status.
	/// </summary>
	public long StatusId { get; init; }

	/// <summary>
	/// Gets the free-text notes the submitter provided.
	/// </summary>
	public string? SubmitterNotes { get; init; }

	/// <summary>
	/// Gets the accumulated moderator notes.
	/// </summary>
	public string? ReviewerNotes { get; init; }

	/// <summary>
	/// Gets the reason recorded when StatusId points at REJECTED.
	/// </summary>
	public string? RejectionReason { get; init; }

	/// <summary>
	/// Gets the user identifier who submitted this row.
	/// </summary>
	public Guid SubmittedByUserId { get; init; }

	/// <summary>
	/// Gets the user identifier who most recently updated this row, if any.
	/// </summary>
	public Guid? LastUpdatedByUserId { get; init; }

	/// <summary>
	/// Gets the date and time when this submission was created.
	/// </summary>
	public DateTimeOffset DateCreated { get; init; }

	/// <summary>
	/// Gets the date and time when StatusId most recently changed, if applicable.
	/// </summary>
	public DateTimeOffset? DateStatusChanged { get; init; }

	/// <summary>
	/// Gets the date and time when this submission was deleted, if applicable.
	/// </summary>
	public DateTimeOffset? DateDeleted { get; set; }
}
