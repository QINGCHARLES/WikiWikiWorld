namespace WikiWikiWorld.Web.Configuration;

/// <summary>
/// Represents the site configuration settings.
/// </summary>
public sealed class SiteConfiguration
{
	/// <summary>
	/// Gets or sets the list of configured sites.
	/// </summary>
	public List<SiteInfo> Sites { get; set; } = [];

	/// <summary>
	/// Gets or sets the list of approved external domains that do not require nofollow on links.
	/// </summary>
	public List<string> ApprovedLinkDomains { get; set; } = [];
}

/// <summary>
/// Represents configuration information for a specific site.
/// </summary>
public sealed class SiteInfo
{
	/// <summary>
	/// Gets or sets the unique identifier for the site.
	/// </summary>
	public int SiteId { get; set; }

	/// <summary>
	/// Gets or sets the display name of the site.
	/// Used in stub banners and other user-facing messages.
	/// </summary>
	public string SiteName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the default culture code for the site.
	/// Required for single-culture sites where the root domain serves content directly.
	/// Optional for multi-culture sites with <see cref="RootDomainIsCultureSelectorOnly"/> set to true.
	/// </summary>
	public string? DefaultCulture { get; set; } = null;

	/// <summary>
	/// Gets or sets whether the root domain only displays a culture selector page.
	/// When true, content is served from culture subdomains, and the root domain shows a culture selection page with minimal sitemap/robots.txt.
	/// When false, the root domain serves wiki content directly using <see cref="DefaultCulture"/>.
	/// </summary>
	public bool RootDomainIsCultureSelectorOnly { get; set; } = false;

	/// <summary>
	/// Gets or sets the list of domains associated with the site.
	/// </summary>
	public List<string> Domains { get; set; } = [];
}
