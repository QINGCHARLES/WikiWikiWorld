using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Service to resolve the current site and culture based on the request.
/// </summary>
public sealed class SiteResolverService
{
	private readonly SiteConfiguration SiteConfiguration;
	private readonly IHttpContextAccessor HttpContextAccessor;

	/// <summary>
	/// Initializes a new instance of the <see cref="SiteResolverService"/> class.
	/// </summary>
	/// <param name="SiteConfigurationOptions">The site configuration options.</param>
	/// <param name="HttpContextAccessor">The HTTP context accessor.</param>
	public SiteResolverService(IOptions<SiteConfiguration> SiteConfigurationOptions, IHttpContextAccessor HttpContextAccessor)
	{
		this.SiteConfiguration = SiteConfigurationOptions.Value;
		this.HttpContextAccessor = HttpContextAccessor;
	}

	/// <summary>
	/// Resolves the site ID and culture from the current request.
	/// </summary>
	/// <returns>A tuple containing the site ID and culture. Culture may be empty for culture-selector root domains.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the HTTP context is missing or no matching domain is found.</exception>
	public (int SiteId, string Culture) ResolveSiteAndCulture()
	{
		HttpContext? Context = HttpContextAccessor.HttpContext;
		if (Context is null)
		{
			throw new InvalidOperationException("HTTP context is not available.");
		}

		string Host = Context.Request.Host.Host;
		string BaseDomain = GetBaseDomain(Host);

		if (string.IsNullOrEmpty(BaseDomain))
		{
			throw new InvalidOperationException($"No matching domain configuration found for host: {Host}");
		}

		SiteInfo? MatchingSite = FindMatchingSite(BaseDomain);
		if (MatchingSite == null)
		{
			throw new InvalidOperationException($"Failed to find matching site for domain: {BaseDomain}");
		}

		string Culture = ExtractCultureFromHost(Host, BaseDomain);
		
		// If no culture subdomain, try default culture
		if (string.IsNullOrEmpty(Culture) && !string.IsNullOrEmpty(MatchingSite.DefaultCulture))
		{
			Culture = MatchingSite.DefaultCulture;
		}

		// For culture-selector root domains without DefaultCulture, return empty culture
		// This allows the CultureSelect page to render on the root domain
		if (string.IsNullOrEmpty(Culture) && MatchingSite.RootDomainIsCultureSelectorOnly)
		{
			return (MatchingSite.SiteId, string.Empty);
		}

		// For non-culture-selector sites, culture is required
		if (string.IsNullOrEmpty(Culture))
		{
			throw new InvalidOperationException($"No culture specified in URL and no default culture configured for site: {MatchingSite.SiteId}");
		}

		return (MatchingSite.SiteId, Culture);
	}

	/// <summary>
	/// Resolves the site ID and culture from the current request, also indicating if this is a culture-selector root domain.
	/// </summary>
	/// <returns>A tuple containing the site ID, culture, and whether this is a culture-selector root domain (should show minimal sitemap/robots.txt).</returns>
	/// <exception cref="InvalidOperationException">Thrown when the HTTP context is missing, no matching domain is found, or no culture is specified.</exception>
	public (int SiteId, string Culture, bool IsCultureSelectorRootDomain) ResolveSiteAndCultureWithRootCheck()
	{
		HttpContext? Context = HttpContextAccessor.HttpContext;
		if (Context is null)
		{
			throw new InvalidOperationException("HTTP context is not available.");
		}

		string Host = Context.Request.Host.Host;
		string BaseDomain = GetBaseDomain(Host);

		if (string.IsNullOrEmpty(BaseDomain))
		{
			throw new InvalidOperationException($"No matching domain configuration found for host: {Host}");
		}

		SiteInfo? MatchingSite = FindMatchingSite(BaseDomain);
		if (MatchingSite == null)
		{
			throw new InvalidOperationException($"Failed to find matching site for domain: {BaseDomain}");
		}

		// Extract culture from subdomain - tells us if we're on the root domain vs a culture subdomain
		string ExtractedCulture = ExtractCultureFromHost(Host, BaseDomain);
		bool IsOnRootDomain = string.IsNullOrEmpty(ExtractedCulture);

		// This is true when:
		// 1. We're accessing the root domain (no culture subdomain in URL), AND
		// 2. The site is configured to show a culture selector on root domain
		// In this case, the sitemap and robots.txt should be minimal
		bool IsCultureSelectorRootDomain = IsOnRootDomain && MatchingSite.RootDomainIsCultureSelectorOnly;

		// Use the extracted culture if present, otherwise fall back to default
		string Culture = ExtractedCulture;
		if (string.IsNullOrEmpty(Culture) && !string.IsNullOrEmpty(MatchingSite.DefaultCulture))
		{
			Culture = MatchingSite.DefaultCulture;
		}

		// For culture-selector sites accessed on root domain, we still need a culture for the return value
		// but the IsCultureSelectorRootDomain flag tells callers to use minimal sitemap/robots
		if (string.IsNullOrEmpty(Culture) && !IsCultureSelectorRootDomain)
		{
			throw new InvalidOperationException($"No culture specified in URL and no default culture configured for site: {MatchingSite.SiteId}");
		}

		// If culture is still empty (culture-selector site with no default), use empty string
		// The caller should check IsCultureSelectorRootDomain and not try to serve content
		Culture ??= string.Empty;

		return (MatchingSite.SiteId, Culture, IsCultureSelectorRootDomain);
	}

	private string GetBaseDomain(string Host)
	{
		if (Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
			Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
		{
			return "localhost";
		}

		foreach (SiteInfo Site in SiteConfiguration.Sites)
		{
			foreach (string Domain in Site.Domains)
			{
				if (Host.Equals(Domain, StringComparison.OrdinalIgnoreCase) ||
					Host.EndsWith($".{Domain}", StringComparison.OrdinalIgnoreCase))
				{
					return Domain;
				}
			}
		}

		return string.Empty;
	}

	private static string ExtractCultureFromHost(string Host, string BaseDomain)
	{
		if (Host.Equals(BaseDomain, StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}

		string DomainWithDot = $".{BaseDomain}";
		if (Host.EndsWith(DomainWithDot, StringComparison.OrdinalIgnoreCase))
		{
			return Host[..^DomainWithDot.Length];
		}

		return string.Empty;
	}

	private SiteInfo? FindMatchingSite(string Domain)
	{
		return SiteConfiguration.Sites.FirstOrDefault(Site =>
			Site.Domains.Any(D => D.Equals(Domain, StringComparison.OrdinalIgnoreCase)));
	}
}
