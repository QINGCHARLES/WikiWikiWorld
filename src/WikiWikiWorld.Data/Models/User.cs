using Microsoft.AspNetCore.Identity;

namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a user in the application.
/// </summary>
public sealed class User : IdentityUser<Guid>
{
    /// <summary>
    /// Gets or sets the GUID of the user's profile picture.
    /// </summary>
    public string? ProfilePicGuid { get; set; }

    /// <summary>
    /// Gets or sets the date and time when this user was created.
    /// </summary>
    public DateTimeOffset DateCreated { get; set; }

    /// <summary>
    /// Gets or sets the date and time when this user was deleted, if applicable.
    /// </summary>
    public DateTimeOffset? DateDeleted { get; set; }
}