namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a citation in an article.
/// </summary>
public sealed record Citation
{
    /// <summary>
    /// Gets the citation number.
    /// </summary>
	public required int Number { get; init; }

    /// <summary>
    /// Gets the unique identifier for the citation.
    /// </summary>
	public required string Id { get; init; }

    /// <summary>
    /// Gets the properties of the citation.
    /// </summary>
	public required Dictionary<string, List<string>> Properties { get; init; }

    /// <summary>
    /// Gets the list of references that use this citation.
    /// </summary>
	public List<string> ReferencedBy { get; init; } = [];
}
