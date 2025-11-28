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
	/// <returns>A tuple containing the site ID and culture.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the HTTP context is missing, no matching domain is found, or no culture is specified.</exception>
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
		if (string.IsNullOrEmpty(Culture) && !string.IsNullOrEmpty(MatchingSite.DefaultCulture))
		{
			Culture = MatchingSite.DefaultCulture;
		}

		if (string.IsNullOrEmpty(Culture))
		{
			throw new InvalidOperationException($"No culture specified in URL and no default culture configured for site: {MatchingSite.SiteId}");
		}

		return (MatchingSite.SiteId, Culture);
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
