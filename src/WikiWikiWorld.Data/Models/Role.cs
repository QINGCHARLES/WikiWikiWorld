using Microsoft.AspNetCore.Identity;

namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a role in the application.
/// </summary>
public sealed class Role : IdentityRole<Guid>
{
    /// <summary>
    /// Gets or sets the date and time when this role was created.
    /// </summary>
    public DateTimeOffset DateCreated { get; set; }

    /// <summary>
    /// Gets or sets the date and time when this role was deleted, if applicable.
    /// </summary>
    public DateTimeOffset? DateDeleted { get; set; }
}
