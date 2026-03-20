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
    /// Gets the original filename before any renaming.
    /// </summary>
    public required string OriginalFilename { get; init; }

    /// <summary>
    /// Gets the description of the download, if any.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets a value indicating whether the filename has been changed from the original.
    /// </summary>
    public bool FilenameChanged { get; init; }

    /// <summary>
    /// Gets a value indicating whether this download needs to be redeployed.
    /// </summary>
    public bool NeedsRedeployment { get; init; }

    /// <summary>
    /// Gets the copyright status identifier, if known.
    /// </summary>
    public long? CopyrightStatusId { get; init; }

    /// <summary>
    /// Gets the download URL status identifier, if known.
    /// </summary>
    public long? DownloadUrlStatusId { get; init; }

    /// <summary>
    /// Gets the user identifier who uploaded this download URL.
    /// </summary>
    public Guid UploadedByUserId { get; init; }

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