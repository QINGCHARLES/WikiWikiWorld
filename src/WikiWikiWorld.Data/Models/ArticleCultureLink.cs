namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a link between articles in different cultures.
/// </summary>
public sealed record ArticleCultureLink
{
    /// <summary>
    /// Gets the unique identifier for this link.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the site identifier.
    /// </summary>
    public int SiteId { get; init; }

    /// <summary>
    /// Gets the canonical article identifier.
    /// </summary>
    public Guid CanonicalArticleId { get; init; }

    /// <summary>
    /// Gets the group identifier for the article culture link.
    /// </summary>
    public Guid ArticleCultureLinkGroupId { get; init; }

    /// <summary>
    /// Gets the user identifier who created this link.
    /// </summary>
    public Guid CreatedByUserId { get; init; }

    /// <summary>
    /// Gets the date and time when this link was created.
    /// </summary>
    public DateTimeOffset DateCreated { get; init; }

    /// <summary>
    /// Gets the user identifier who deleted this link, if applicable.
    /// </summary>
    public Guid? DeletedByUserId { get; init; }

    /// <summary>
    /// Gets the date and time when this link was deleted, if applicable.
    /// </summary>
    public DateTimeOffset? DateDeleted { get; init; }
}
