namespace WikiWikiWorld.Data.Models;

public sealed record ArticleRevision
{
    public int Id { get; init; }
    public Guid CanonicalArticleId { get; init; }
    public int SiteId { get; init; }
    public required string Culture { get; init; }
    public required string Title { get; init; }
    public string? DisplayTitle { get; init; }
    public required string UrlSlug { get; init; }
    public bool IsCurrent { get; set; }
    public ArticleType Type { get; init; }
    public Guid? CanonicalFileId { get; init; }
    public required string Text { get; init; }
    public required string RevisionReason { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset DateCreated { get; init; }
    public DateTimeOffset? DateDeleted { get; set; }
}

public enum ArticleType
{
    Article,
    Category,
    File,
    User,
    Info
}