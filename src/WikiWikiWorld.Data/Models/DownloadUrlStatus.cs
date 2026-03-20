namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents the deployment/review status of a download URL.
/// </summary>
public sealed record DownloadUrlStatus
{
	/// <summary>
	/// Gets the unique identifier for this download URL status.
	/// </summary>
	public long Id { get; init; }

	/// <summary>
	/// Gets the status name (e.g., RECEIVED, VERIFIED, REJECTED, DEPLOYED, DEPLOYERROR, UNKNOWN).
	/// </summary>
	public required string Status { get; init; }
}
