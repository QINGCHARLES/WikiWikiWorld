using Microsoft.AspNetCore.Identity;

namespace WikiWikiWorld.Data.Models;

public sealed class ApplicationUser : IdentityUser<Guid>
{
	public override Guid Id { get; set; } = Guid.NewGuid(); // Matches DB Schema
	public override string? UserName { get; set; }
	public override string? NormalizedUserName { get; set; }
	public override string? Email { get; set; }
	public override string? NormalizedEmail { get; set; }
	public override bool EmailConfirmed { get; set; }
	public override string? PasswordHash { get; set; }
	public override string? SecurityStamp { get; set; }
	public override string? ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
	public override bool TwoFactorEnabled { get; set; }
	public override DateTimeOffset? LockoutEnd { get; set; }
	public override bool LockoutEnabled { get; set; }
	public override int AccessFailedCount { get; set; }
	public string? ProfilePicGuid { get; set; } // New property for profile picture GUID
	public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset? DateDeleted { get; set; }
}