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

    /// <summary>
    /// Gets the priority of the category.
    /// </summary>
    public PriorityOptions Priority { get; init; }

    /// <summary>
    /// Defines the priority options for a category.
    /// </summary>
    public enum PriorityOptions
    {
        /// <summary>
        /// Primary priority.
        /// </summary>
        Primary,

        /// <summary>
        /// Secondary priority.
        /// </summary>
        Secondary
    }
}
