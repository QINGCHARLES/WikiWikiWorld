using Microsoft.AspNetCore.Http;
using WikiWikiWorld.Data;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Provides site context from the current HTTP request for multi-tenant query filtering.
/// </summary>
/// <remarks>
/// Resolution is deferred until first use so the service can be constructed in scopes that have no
/// HTTP request (notably the startup maintenance scope in Program.cs). When there is no HttpContext,
/// the service reports cross-site/culture mode so the DbContext skips strict site/culture validation.
/// </remarks>
public sealed class SiteContextService : ISiteContextService
{
	private readonly SiteResolverService _siteResolver;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private bool _resolved;
	private int? _currentSiteId;
	private string? _currentCulture;

	private bool _isCultureSelectorRootDomain;

	/// <summary>
	/// Initializes a new instance of the <see cref="SiteContextService"/> class.
	/// </summary>
	/// <param name="SiteResolver">The site resolver service.</param>
	/// <param name="HttpContextAccessor">The HTTP context accessor.</param>
	public SiteContextService(SiteResolverService SiteResolver, IHttpContextAccessor HttpContextAccessor)
	{
		_siteResolver = SiteResolver;
		_httpContextAccessor = HttpContextAccessor;
	}

	/// <inheritdoc/>
	public bool AllowCrossSiteAndCultureQueries
	{
		get
		{
			EnsureResolved();
			return _httpContextAccessor.HttpContext is null || _isCultureSelectorRootDomain;
		}
	}

	/// <inheritdoc/>
	public int? GetCurrentSiteId()
	{
		EnsureResolved();
		return _currentSiteId;
	}

	/// <inheritdoc/>
	public string? GetCurrentCulture()
	{
		EnsureResolved();
		return _currentCulture;
	}

	private void EnsureResolved()
	{
		if (_resolved || _httpContextAccessor.HttpContext is null)
		{
			return;
		}

		(_currentSiteId, _currentCulture, _isCultureSelectorRootDomain) = _siteResolver.ResolveSiteAndCultureWithRootCheck();
		_resolved = true;
	}
}
