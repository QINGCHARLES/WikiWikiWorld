using Microsoft.AspNetCore.Identity;

namespace WikiWikiWorld.Data.Models;

public sealed class Role : IdentityRole<Guid>
{
    public DateTimeOffset DateCreated { get; set; }
    public DateTimeOffset? DateDeleted { get; set; }
}
