using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.Pages
{
	/// <summary>
	/// Page model for the culture selection page.
	/// </summary>
	/// <param name="SiteResolverService">The site resolver service.</param>
	/// <param name="SiteConfigurationOptions">The site configuration options.</param>
	public sealed class CultureSelectModel(
		SiteResolverService SiteResolverService,
		IOptions<SiteConfiguration> SiteConfigurationOptions) : BasePageModel(SiteResolverService)
	{
		private readonly SiteConfiguration SiteConfiguration = SiteConfigurationOptions.Value;

		/// <inheritdoc/>
		protected override bool AllowsCultureSelectorRootDomain => true;

		/// <summary>
		/// Gets the available culture links for the current site.
		/// </summary>
		public IReadOnlyList<CultureLink> CultureLinks { get; private set; } = [];

		/// <summary>
		/// Handles the GET request for the culture selection page.
		/// </summary>
		public void OnGet()
		{
			SiteInfo CurrentSite = SiteConfiguration.Sites.First(Site => Site.SiteId == SiteId);
			string RootDomain = SiteCultureHelper.GetRootDomain(CurrentSite, Request.Host.Host);

			CultureLinks = [.. CurrentSite.Domains
				.Where(Domain => Domain.EndsWith($".{RootDomain}", StringComparison.OrdinalIgnoreCase))
				.Select(Domain =>
				{
					string CultureCode = Domain[..^(RootDomain.Length + 1)];
					return new CultureLink(
						CultureCode,
						SiteCultureHelper.GetDisplayName(CultureCode),
						SiteCultureHelper.BuildCultureOrigin(CurrentSite, RootDomain, Request.Scheme, Request.Host.Port, CultureCode));
				})
				.OrderBy(Link => Link.DisplayName, StringComparer.OrdinalIgnoreCase)];
		}
	}

	/// <summary>
	/// Represents a culture selector link.
	/// </summary>
	/// <param name="CultureCode">The culture code.</param>
	/// <param name="DisplayName">The user-facing display name.</param>
	/// <param name="Url">The destination URL.</param>
	public sealed record CultureLink(string CultureCode, string DisplayName, string Url);
}
