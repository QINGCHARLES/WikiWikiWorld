namespace WikiWikiWorld.Data.Models;

public sealed record ArticleTalkSubject
{
    public Guid Id { get; init; }
    public int SiteId { get; init; }
    public Guid CanonicalArticleId { get; init; }
    public required string Subject { get; init; }
    public required string UrlSlug { get; init; }
    public bool HasBeenEdited { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset DateCreated { get; init; }
    public DateTimeOffset? DateModified { get; init; }
    public DateTimeOffset? DateDeleted { get; init; }
}
