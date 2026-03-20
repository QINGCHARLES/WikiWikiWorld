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
			string CurrentHost = Request.Host.Host;
			SiteInfo CurrentSite = SiteConfiguration.Sites.First(Site => Site.SiteId == SiteId);
			string RootDomain = GetRootDomain(CurrentSite, CurrentHost);
			int? Port = Request.Host.Port;
			string Scheme = Request.Scheme;

			CultureLinks = [.. CurrentSite.Domains
				.Where(Domain => Domain.EndsWith($".{RootDomain}", StringComparison.OrdinalIgnoreCase))
				.Select(Domain =>
				{
					string CultureCode = Domain[..^(RootDomain.Length + 1)];
					string Host = Port.HasValue
						? $"{CultureCode}.{RootDomain}:{Port.Value}"
						: $"{CultureCode}.{RootDomain}";

					return new CultureLink(
						CultureCode,
						GetCultureDisplayName(CultureCode),
						$"{Scheme}://{Host}");
				})
				.OrderBy(Link => Link.DisplayName, StringComparer.OrdinalIgnoreCase)];
		}

		/// <summary>
		/// Gets the root domain for the current site and request host.
		/// </summary>
		/// <param name="CurrentSite">The current site configuration.</param>
		/// <param name="CurrentHost">The current request host name.</param>
		/// <returns>The root domain for the current request.</returns>
		/// <exception cref="InvalidOperationException">Thrown when a root domain cannot be determined.</exception>
		private static string GetRootDomain(SiteInfo CurrentSite, string CurrentHost)
		{
			string? RootDomain = CurrentSite.Domains
				.Where(Domain => CurrentHost.Equals(Domain, StringComparison.OrdinalIgnoreCase))
				.OrderBy(Domain => Domain.Length)
				.FirstOrDefault();

			if (RootDomain is not null)
			{
				return RootDomain;
			}

			RootDomain = CurrentSite.Domains
				.Where(Domain => CurrentHost.EndsWith($".{Domain}", StringComparison.OrdinalIgnoreCase))
				.OrderBy(Domain => Domain.Length)
				.FirstOrDefault();

			if (RootDomain is not null)
			{
				return RootDomain;
			}

			throw new InvalidOperationException($"Unable to determine the root domain for host '{CurrentHost}'.");
		}

		/// <summary>
		/// Gets a display name for the specified culture code.
		/// </summary>
		/// <param name="CultureCode">The culture code.</param>
		/// <returns>The user-facing display name.</returns>
		private static string GetCultureDisplayName(string CultureCode)
		{
			try
			{
				CultureInfo CultureInfo = CultureInfo.GetCultureInfo(CultureCode);
				return CultureInfo.EnglishName;
			}
			catch (CultureNotFoundException)
			{
				return CultureCode;
			}
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
