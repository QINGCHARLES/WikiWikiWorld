namespace WikiWikiWorld.Data.Models;

public sealed record FileRevision
{
    public int Id { get; init; }
    public Guid CanonicalFileId { get; init; }
    public bool? IsCurrent { get; set; }
    public FileType Type { get; init; }
    public required string Filename { get; init; }
    public required string MimeType { get; init; }
    public long FileSizeBytes { get; init; }
    public string? Source { get; init; }
    public required string RevisionReason { get; init; }
    public required string SourceAndRevisionReasonCulture { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset DateCreated { get; init; }
    public DateTimeOffset? DateDeleted { get; set; }
}

public enum FileType
{
    Image2D,
    Video,
    Audio
}