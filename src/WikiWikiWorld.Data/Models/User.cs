using Microsoft.AspNetCore.Identity;

namespace WikiWikiWorld.Data.Models;

public sealed class User : IdentityUser<Guid>
{
    public string? ProfilePicGuid { get; set; }
    public DateTimeOffset DateCreated { get; set; }
    public DateTimeOffset? DateDeleted { get; set; }
}