namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents a domain redirect mapping that routes an incoming domain to a specific article and culture.
/// </summary>
public sealed record ArticleDomainRedirect
{
	/// <summary>
	/// Gets the unique identifier for this domain redirect.
	/// </summary>
	public int Id { get; init; }

	/// <summary>
	/// Gets the domain name that triggers the redirect (e.g., "vintagecomputers.org").
	/// Stored in normalized lowercase without scheme, path, or port.
	/// </summary>
	public required string Domain { get; init; }

	/// <summary>
	/// Gets the target site identifier.
	/// </summary>
	public int SiteId { get; init; }

	/// <summary>
	/// Gets the target culture code (e.g., "en", "ja").
	/// </summary>
	public required string Culture { get; init; }

	/// <summary>
	/// Gets the canonical article identifier of the target article.
	/// Survives article renames and title updates.
	/// </summary>
	public Guid CanonicalArticleId { get; init; }

	/// <summary>
	/// Gets the identifier of the user who created this mapping.
	/// </summary>
	public Guid CreatedByUserId { get; init; }

	/// <summary>
	/// Gets the date and time when this redirect was created.
	/// </summary>
	public DateTimeOffset DateCreated { get; init; }

	/// <summary>
	/// Gets or sets the date and time when this redirect was deleted, if applicable.
	/// </summary>
	public DateTimeOffset? DateDeleted { get; set; }
}
