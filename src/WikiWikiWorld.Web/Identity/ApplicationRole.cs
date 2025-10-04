using Microsoft.AspNetCore.Identity;

namespace WikiWikiWorld.Data.Models;

public sealed class ApplicationRole : IdentityRole<Guid>
{
	public override Guid Id { get; set; } = Guid.NewGuid(); // Matches DB Schema
	public override string? Name { get; set; }
	public override string? NormalizedName { get; set; }
	public override string? ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();

	public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset? DateDeleted { get; set; }
}
