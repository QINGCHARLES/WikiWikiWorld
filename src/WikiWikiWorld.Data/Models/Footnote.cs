namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a footnote in an article.
/// </summary>
public sealed record Footnote
{
    /// <summary>
    /// Gets the footnote number.
    /// </summary>
    public required int Number { get; init; }

    /// <summary>
    /// Gets the text content of the footnote.
    /// </summary>
    public required string Text { get; init; }
}
