using WikiWikiWorld.Data;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Provides site context from the current HTTP request for multi-tenant query filtering.
/// </summary>
public sealed class SiteContextService : ISiteContextService
{
	private readonly int? _currentSiteId;
	private readonly string? _currentCulture;

	/// <summary>
	/// Initializes a new instance of the <see cref="SiteContextService"/> class.
	/// </summary>
	/// <param name="SiteResolver">The site resolver service.</param>
	public SiteContextService(SiteResolverService SiteResolver)
	{
		(int SiteId, string Culture) = SiteResolver.ResolveSiteAndCulture();
		_currentSiteId = SiteId;
		_currentCulture = Culture;
	}

	/// <inheritdoc/>
	public int? GetCurrentSiteId() => _currentSiteId;

	/// <inheritdoc/>
	public string? GetCurrentCulture() => _currentCulture;
}
