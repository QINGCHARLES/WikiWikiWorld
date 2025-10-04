namespace WikiWikiWorld.Data.Models;

public sealed record User
{
    public Guid Id { get; init; }
    public required string UserName { get; init; }
    public required string NormalizedUserName { get; init; }
    public required string Email { get; init; }
    public required string NormalizedEmail { get; init; }
    public bool EmailConfirmed { get; init; }
    public required string PasswordHash { get; init; }
    public required string SecurityStamp { get; init; }
    public required string ConcurrencyStamp { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    public bool LockoutEnabled { get; init; }
    public int AccessFailedCount { get; init; }
    public string? ProfilePicGuid { get; init; }
    public DateTimeOffset DateCreated { get; init; }
    public DateTimeOffset? DateDeleted { get; init; }
}