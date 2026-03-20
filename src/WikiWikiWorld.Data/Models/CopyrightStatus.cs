namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a copyright status for download files.
/// </summary>
public sealed record CopyrightStatus
{
    /// <summary>
    /// Gets the unique identifier for this copyright status.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// Gets the status name (e.g., COPYRIGHTHOLDER, SHAREPERMITTED, PUBLICDOMAIN).
    /// </summary>
    public required string Status { get; init; }
}
