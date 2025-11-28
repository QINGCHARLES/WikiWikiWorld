namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a link between a user and a role.
/// </summary>
public sealed record UserRole
{
    /// <summary>
    /// Gets the user identifier.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the role identifier.
    /// </summary>
    public Guid RoleId { get; init; }

    /// <summary>
    /// Gets the date and time when this link was created.
    /// </summary>
    public DateTimeOffset DateCreated { get; init; }

    /// <summary>
    /// Gets the date and time when this link was deleted, if applicable.
    /// </summary>
    public DateTimeOffset? DateDeleted { get; init; }
}
