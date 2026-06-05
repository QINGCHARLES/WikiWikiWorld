namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a copyright-exemption category a submitter can pick when offering a publication for ingestion.
/// </summary>
public sealed record PublicationUploadCopyrightExemption
{
	/// <summary>
	/// Gets the unique identifier for this exemption.
	/// </summary>
	public long Id { get; init; }

	/// <summary>
	/// Gets the stable code (e.g., PUBLICDOMAIN, FAIRUSE). Display strings live in the UI resource files.
	/// </summary>
	public required string Code { get; init; }
}
