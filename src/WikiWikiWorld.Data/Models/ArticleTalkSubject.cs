namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a subject in an article's talk page.
/// </summary>
public sealed record ArticleTalkSubject
{
    /// <summary>
    /// Gets the unique identifier for this subject.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the site identifier.
    /// </summary>
    public int SiteId { get; init; }

    /// <summary>
    /// Gets the canonical article identifier.
    /// </summary>
    public Guid CanonicalArticleId { get; init; }

    /// <summary>
    /// Gets the subject text.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Gets the URL slug for the subject.
    /// </summary>
    public required string UrlSlug { get; init; }

    /// <summary>
    /// Gets a value indicating whether this subject has been edited.
    /// </summary>
    public bool HasBeenEdited { get; init; }

    /// <summary>
    /// Gets the user identifier who created this subject.
    /// </summary>
    public Guid CreatedByUserId { get; init; }

    /// <summary>
    /// Gets the date and time when this subject was created.
    /// </summary>
    public DateTimeOffset DateCreated { get; init; }

    /// <summary>
    /// Gets the date and time when this subject was last modified, if applicable.
    /// </summary>
    public DateTimeOffset? DateModified { get; init; }

    /// <summary>
    /// Gets the date and time when this subject was deleted, if applicable.
    /// </summary>
    public DateTimeOffset? DateDeleted { get; init; }
}
