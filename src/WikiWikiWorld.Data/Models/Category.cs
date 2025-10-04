namespace WikiWikiWorld.Data.Models;

public sealed record Category
{
    public required string Title { get; init; }
    public string? UrlSlug { get; init; }
    public PriorityOptions Priority { get; init; }

    public enum PriorityOptions
    {
        Primary,
        Secondary
    }
}
