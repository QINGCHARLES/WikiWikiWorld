namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a category for an article.
/// </summary>
public sealed record Category
{
    /// <summary>
    /// Gets the title of the category.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the URL slug for the category.
    /// </summary>
    public string? UrlSlug { get; init; }
}
