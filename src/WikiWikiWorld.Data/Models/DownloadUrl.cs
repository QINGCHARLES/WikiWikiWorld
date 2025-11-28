namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a download URL for a file.
/// </summary>
public sealed record DownloadUrl
{
    /// <summary>
    /// Gets the unique identifier for this download URL.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// Gets the site identifier.
    /// </summary>
    public int SiteId { get; init; }

    /// <summary>
    /// Gets the SHA-256 hash of the file.
    /// </summary>
    public required string HashSha256 { get; init; }

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
    /// Gets the download URLs as a string.
    /// </summary>
    public string? DownloadUrls { get; init; }

    /// <summary>
    /// Gets the quality of the file, if applicable.
    /// </summary>
    public int? Quality { get; init; }

    /// <summary>
    /// Gets a value indicating whether the file needs OCR.
    /// </summary>
    public bool? NeedsOcr { get; init; }

    /// <summary>
    /// Gets a value indicating whether the download is complete.
    /// </summary>
    public bool? IsComplete { get; init; }

    /// <summary>
    /// Gets the user identifier who created this download URL.
    /// </summary>
    public Guid CreatedByUserId { get; init; }

    /// <summary>
    /// Gets the date and time when this download URL was created.
    /// </summary>
    public DateTimeOffset DateCreated { get; init; }

    /// <summary>
    /// Gets the date and time when this download URL was last modified, if applicable.
    /// </summary>
    public DateTimeOffset? DateModified { get; init; }

    /// <summary>
    /// Gets the date and time when this download URL was deleted, if applicable.
    /// </summary>
    public DateTimeOffset? DateDeleted { get; init; }
}