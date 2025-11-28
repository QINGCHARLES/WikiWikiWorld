namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a revision of an article.
/// </summary>
public sealed record ArticleRevision
{
    /// <summary>
    /// Gets the unique identifier for this revision.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the canonical article identifier.
    /// </summary>
    public Guid CanonicalArticleId { get; init; }

    /// <summary>
    /// Gets the site identifier.
    /// </summary>
    public int SiteId { get; init; }

    /// <summary>
    /// Gets the culture code (e.g., "en-US").
    /// </summary>
    public required string Culture { get; init; }

    /// <summary>
    /// Gets the title of the article.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the display title of the article, if different from the title.
    /// </summary>
    public string? DisplayTitle { get; init; }

    /// <summary>
    /// Gets the URL slug for the article.
    /// </summary>
    public required string UrlSlug { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the current revision.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// Gets the type of the article.
    /// </summary>
    public ArticleType Type { get; init; }

    /// <summary>
    /// Gets the canonical file identifier, if this article represents a file.
    /// </summary>
    public Guid? CanonicalFileId { get; init; }

    /// <summary>
    /// Gets the text content of the article.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the reason for this revision.
    /// </summary>
    public required string RevisionReason { get; init; }

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
/// Defines the types of articles.
/// </summary>
public enum ArticleType
{
    /// <summary>
    /// A standard article.
    /// </summary>
    Article,

    /// <summary>
    /// A category page.
    /// </summary>
    Category,

    /// <summary>
    /// A file page.
    /// </summary>
    File,

    /// <summary>
    /// A user page.
    /// </summary>
    User,

    /// <summary>
    /// An information page.
    /// </summary>
    Info
}