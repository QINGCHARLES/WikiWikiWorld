namespace WikiWikiWorld.Data;

/// <summary>
/// Provides the current site context for multi-tenant query filtering.
/// </summary>
public interface ISiteContextService
{
	/// <summary>
	/// Gets the current site identifier, or null if not in a site context.
	/// </summary>
	/// <returns>The site ID, or null for cross-site operations.</returns>
	int? GetCurrentSiteId();

	/// <summary>
	/// Gets the current culture, or null if not in a culture context.
	/// </summary>
	/// <returns>The culture code, or null for cross-culture operations.</returns>
	string? GetCurrentCulture();
}
