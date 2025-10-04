using Microsoft.AspNetCore.Identity;

namespace WikiWikiWorld.Data.Models;

public sealed class ApplicationUserRole : IdentityUserRole<Guid>
{
	public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset? DateDeleted { get; set; }
}
