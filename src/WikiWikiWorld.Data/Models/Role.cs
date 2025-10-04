namespace WikiWikiWorld.Data.Models;

public sealed record Role
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string NormalizedName { get; init; }
    public required string ConcurrencyStamp { get; init; }
    public DateTimeOffset DateCreated { get; init; }
    public DateTimeOffset? DateDeleted { get; init; }
}
