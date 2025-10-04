namespace WikiWikiWorld.Data.Models;

public sealed record UserRole
{
    public Guid UserId { get; init; }
    public Guid RoleId { get; init; }
    public DateTimeOffset DateCreated { get; init; }
    public DateTimeOffset? DateDeleted { get; init; }
}
