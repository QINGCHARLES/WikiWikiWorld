namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a revision of a file.
/// </summary>
public sealed record FileRevision
{
    /// <summary>
    /// Gets the unique identifier for this revision.
    /// </summary>
    public int Id { get; init; }
    /// <summary>
    /// Gets the canonical file identifier.
    /// </summary>
    public Guid CanonicalFileId { get; init; }
    /// <summary>
    /// Gets or sets a value indicating whether this is the current revision.
    /// </summary>
    public bool? IsCurrent { get; set; }
    /// <summary>
    /// Gets the type of the file.
    /// </summary>
    public FileType Type { get; init; }
    /// <summary>
    /// Gets the filename.
    /// </summary>
    public required string Filename { get; init; }
    /// <summary>
    /// Gets the MIME type of the file.
    /// </summary>
    public required string MimeType { get; init; }
    /// <summary>
    /// Gets the size of the file in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }
    /// <summary>
    /// Gets the source of the file.
    /// </summary>
    public string? Source { get; init; }
    /// <summary>
    /// Gets the reason for this revision.
    /// </summary>
    public required string RevisionReason { get; init; }
    /// <summary>
    /// Gets the culture of the source and revision reason.
    /// </summary>
    public required string SourceAndRevisionReasonCulture { get; init; }
    /// <summary>
    /// Gets the user identifier who created this revision.
    /// </summary>
    public Guid CreatedByUserId { get; init; }
    /// <summary>
    /// Gets the date and time when this revision was created.
    /// </summary>
    public DateTimeOffset DateCreated { get; init; }
    /// <summary>
    /// Gets or sets the date and time when this revision was deleted, if applicable.
    /// </summary>
    public DateTimeOffset? DateDeleted { get; set; }
}

/// <summary>
/// Defines the types of files.
/// </summary>
public enum FileType
{
    /// <summary>
    /// A 2D image.
    /// </summary>
    Image2D,
    /// <summary>
    /// A video file.
    /// </summary>
    Video,
    /// <summary>
    /// An audio file.
    /// </summary>
    Audio
}