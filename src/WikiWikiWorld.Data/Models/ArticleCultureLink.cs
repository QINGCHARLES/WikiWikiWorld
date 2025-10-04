namespace WikiWikiWorld.Data.Models;

public sealed record ArticleCultureLink
{
    public int Id { get; init; }
    public int SiteId { get; init; }
    public Guid CanonicalArticleId { get; init; }
    public Guid ArticleCultureLinkGroupId { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset DateCreated { get; init; }
    public Guid? DeletedByUserId { get; init; }
    public DateTimeOffset? DateDeleted { get; init; }
}
