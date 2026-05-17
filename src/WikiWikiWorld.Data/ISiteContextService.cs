namespace WikiWikiWorld.Data;

/// <summary>
/// Provides the current site context for multi-tenant query filtering.
/// </summary>
public interface ISiteContextService
{
	/// <summary>
	/// Gets a value indicating whether queries may intentionally cross site and culture boundaries.
	/// </summary>
	bool AllowCrossSiteAndCultureQueries => false;

	/// <summary>
	/// Gets the current site identifier.
	/// </summary>
	/// <returns>The site ID, or null only when <see cref="AllowCrossSiteAndCultureQueries"/> is true.</returns>
	int? GetCurrentSiteId();

	/// <summary>
	/// Gets the current culture.
	/// </summary>
	/// <returns>The culture code, or null only when <see cref="AllowCrossSiteAndCultureQueries"/> is true.</returns>
	string? GetCurrentCulture();
}
