namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a post within a talk subject.
/// </summary>
public sealed record ArticleTalkSubjectPost
{
    /// <summary>
    /// Gets the unique identifier for this post.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the identifier of the talk subject this post belongs to.
    /// </summary>
    public int ArticleTalkSubjectId { get; init; }

    /// <summary>
    /// Gets the identifier of the parent post, if this is a reply.
    /// </summary>
    public int? ParentTalkSubjectPostId { get; init; }

    /// <summary>
    /// Gets the text content of the post.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets a value indicating whether this post has been edited.
    /// </summary>
    public bool HasBeenEdited { get; init; }

    /// <summary>
    /// Gets the user identifier who created this post.
    /// </summary>
    public Guid CreatedByUserId { get; init; }

    /// <summary>
    /// Gets the date and time when this post was created.
    /// </summary>
    public DateTimeOffset DateCreated { get; init; }

    /// <summary>
    /// Gets the date and time when this post was last modified, if applicable.
    /// </summary>
    public DateTimeOffset? DateModified { get; init; }

    /// <summary>
    /// Gets the date and time when this post was deleted, if applicable.
    /// </summary>
    public DateTimeOffset? DateDeleted { get; init; }
}

