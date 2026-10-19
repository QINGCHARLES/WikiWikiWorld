namespace WikiWikiWorld.Web.Configuration;

/// <summary>
/// Provides host-canonicalization redirect settings applied before routing.
/// When the section is absent or empty, no host redirects are performed.
/// </summary>
public sealed class DomainRedirectOptions
{
	/// <summary>
	/// Gets the configuration section name for these options.
	/// </summary>
	public const string SectionName = "DomainRedirects";

	/// <summary>
	/// Gets or sets a value indicating whether a leading "www." is stripped from the host with a 301 redirect.
	/// </summary>
	public bool StripWww { get; set; } = false;

	/// <summary>
	/// Gets or sets host suffix mappings applied with a 301 redirect, preserving any subdomain.
	/// For example, mapping "magazedia.wiki" to "magazedia.com" redirects en.magazedia.wiki to en.magazedia.com.
	/// </summary>
	public Dictionary<string, string> HostMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Gets or sets the target host that requests are 308-redirected to when their host
	/// is not configured in <see cref="SiteConfiguration"/> and is not a localhost host.
	/// </summary>
	public string? UnknownHostTarget { get; set; }
}
