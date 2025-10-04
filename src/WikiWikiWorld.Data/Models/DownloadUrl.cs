namespace WikiWikiWorld.Data.Models;

public sealed record DownloadUrl
{
    public long Id { get; init; }
    public int SiteId { get; init; }
    public required string HashSha256 { get; init; }
    public required string Filename { get; init; }
    public required string MimeType { get; init; }
    public long FileSizeBytes { get; init; }
    public string? DownloadUrls { get; init; }
    public int? Quality { get; init; }
    public bool? NeedsOcr { get; init; }
    public bool? IsComplete { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset DateCreated { get; init; }
    public DateTimeOffset? DateModified { get; init; }
    public DateTimeOffset? DateDeleted { get; init; }
}