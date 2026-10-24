using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Derives a site's languages and their per-culture origins from the configured domains,
/// where each culture is served from a <c>{culture}.{root domain}</c> subdomain.
/// </summary>
public static class SiteCultureHelper
{
	/// <summary>
	/// Gets the root domain of a site for the current request host.
	/// </summary>
	/// <param name="Site">The current site configuration.</param>
	/// <param name="CurrentHost">The current request host name, without a port.</param>
	/// <returns>
	/// The shortest configured domain that equals <paramref name="CurrentHost"/> or is a parent of it,
	/// so <c>ja.magazedia.com</c> resolves to <c>magazedia.com</c> even when <c>ja.magazedia.com</c>
	/// is itself configured.
	/// </returns>
	/// <exception cref="InvalidOperationException">Thrown when no configured domain matches the host.</exception>
	public static string GetRootDomain(SiteInfo Site, string CurrentHost)
	{
		ArgumentNullException.ThrowIfNull(Site);

		return Site.Domains
			.Where(Domain => CurrentHost.Equals(Domain, StringComparison.OrdinalIgnoreCase)
				|| CurrentHost.EndsWith($".{Domain}", StringComparison.OrdinalIgnoreCase))
			.OrderBy(Domain => Domain.Length)
			.FirstOrDefault()
			?? throw new InvalidOperationException($"Unable to determine the root domain for host '{CurrentHost}'.");
	}

	/// <summary>
	/// Gets the culture codes a site serves.
	/// </summary>
	/// <param name="Site">The site configuration.</param>
	/// <param name="RootDomain">The site's root domain, from <see cref="GetRootDomain"/>.</param>
	/// <returns>
	/// The subdomain prefix of every configured <c>{culture}.{root domain}</c> domain, plus the site's
	/// default culture when it has one, without duplicates and in configuration order.
	/// </returns>
	public static IReadOnlyList<string> GetCultures(SiteInfo Site, string RootDomain)
	{
		ArgumentNullException.ThrowIfNull(Site);

		IEnumerable<string> SubdomainCultures = Site.Domains
			.Where(Domain => Domain.EndsWith($".{RootDomain}", StringComparison.OrdinalIgnoreCase))
			.Select(Domain => Domain[..^(RootDomain.Length + 1)]);

		IEnumerable<string> DefaultCulture = string.IsNullOrWhiteSpace(Site.DefaultCulture) ? [] : [Site.DefaultCulture];

		return [.. SubdomainCultures.Concat(DefaultCulture).Distinct(StringComparer.OrdinalIgnoreCase)];
	}

	/// <summary>
	/// Builds the scheme, host and port that serve a culture of the current site.
	/// </summary>
	/// <param name="Site">The site configuration.</param>
	/// <param name="RootDomain">The site's root domain, from <see cref="GetRootDomain"/>.</param>
	/// <param name="Scheme">The request scheme, such as <c>https</c>.</param>
	/// <param name="Port">The request port, or <see langword="null"/> for the scheme default.</param>
	/// <param name="CultureCode">The culture to address.</param>
	/// <returns>
	/// An origin without a trailing slash, such as <c>https://ja.localhost:7126</c>. A site's default
	/// culture that has no configured subdomain is addressed at the root domain.
	/// </returns>
	public static string BuildCultureOrigin(SiteInfo Site, string RootDomain, string Scheme, int? Port, string CultureCode)
	{
		ArgumentNullException.ThrowIfNull(Site);
		ArgumentException.ThrowIfNullOrWhiteSpace(CultureCode);

		string CultureHost = $"{CultureCode}.{RootDomain}";
		bool HasCultureSubdomain = Site.Domains.Any(Domain => Domain.Equals(CultureHost, StringComparison.OrdinalIgnoreCase));
		bool IsRootDefaultCulture = !HasCultureSubdomain
			&& !Site.RootDomainIsCultureSelectorOnly
			&& CultureCode.Equals(Site.DefaultCulture, StringComparison.OrdinalIgnoreCase);

		string Host = IsRootDefaultCulture ? RootDomain : CultureHost;
		return Port.HasValue ? $"{Scheme}://{Host}:{Port.Value}" : $"{Scheme}://{Host}";
	}

	/// <summary>
	/// Gets a display name for the specified culture code, in that culture's own language.
	/// </summary>
	/// <param name="CultureCode">The culture code.</param>
	/// <returns>
	/// The endonym for the culture (for example "English", "日本語", "العربية"), or the
	/// culture code itself when no real culture matches it.
	/// </returns>
	/// <remarks>
	/// Requires ICU. On an image built without it (any <c>*-chiseled</c> family that is not
	/// <c>-extra</c>, which sets <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT</c>) the lookup
	/// throws and every language falls back to its bare code.
	/// </remarks>
	public static string GetDisplayName(string CultureCode)
	{
		try
		{
			CultureInfo Culture = CultureInfo.GetCultureInfo(CultureCode);

			// Well-formed but unrecognised tags such as "xx-test" resolve to a synthesised
			// culture rather than throwing, whose name reads worse than the raw code.
			if (Culture.CultureTypes.HasFlag(CultureTypes.UserCustomCulture)
				|| string.IsNullOrWhiteSpace(Culture.NativeName))
			{
				return CultureCode;
			}

			return Culture.NativeName;
		}
		catch (CultureNotFoundException)
		{
			return CultureCode;
		}
	}
}
