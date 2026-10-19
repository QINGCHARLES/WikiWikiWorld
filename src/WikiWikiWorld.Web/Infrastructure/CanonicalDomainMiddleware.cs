using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// Canonicalizes the request host before routing: strips "www.", applies configured host
/// mappings (for example, magazedia.wiki to magazedia.com), redirects unknown hosts to a
/// configured target, and rewrites the root path to the culture selector page for sites
/// whose root domain only serves culture selection.
/// </summary>
public sealed class CanonicalDomainMiddleware
{
	private readonly RequestDelegate Next;
	private readonly DomainRedirectOptions RedirectOptions;
	private readonly SiteConfiguration SiteConfiguration;
	private readonly HashSet<string> KnownHosts;

	/// <summary>
	/// Initializes a new instance of the <see cref="CanonicalDomainMiddleware"/> class.
	/// </summary>
	/// <param name="Next">The next middleware in the pipeline.</param>
	/// <param name="RedirectOptions">The domain redirect options.</param>
	/// <param name="SiteConfigurationOptions">The site configuration options.</param>
	public CanonicalDomainMiddleware(
		RequestDelegate Next,
		IOptions<DomainRedirectOptions> RedirectOptions,
		IOptions<SiteConfiguration> SiteConfigurationOptions)
	{
		this.Next = Next;
		this.RedirectOptions = RedirectOptions.Value;
		this.SiteConfiguration = SiteConfigurationOptions.Value;

		KnownHosts = new HashSet<string>(
			SiteConfiguration.Sites.SelectMany(Site => Site.Domains),
			StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Processes the request, issuing a canonical-host redirect or culture-selector rewrite when required.
	/// </summary>
	/// <param name="Context">The HTTP context.</param>
	/// <param name="DomainRedirectService">The article domain redirect service.</param>
	/// <returns>A task that completes when the request has been handled.</returns>
	public async Task InvokeAsync(HttpContext Context, Services.ArticleDomainRedirectService DomainRedirectService)
	{
		string Host = Context.Request.Host.Host;

		// 1. Strip a leading "www." (301)
		if (RedirectOptions.StripWww &&
			Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) &&
			Host.Length > 4)
		{
			await RedirectToHostAsync(Context, Host[4..], StatusCodes.Status301MovedPermanently);
			return;
		}

		// 2. Host suffix mappings, preserving any subdomain (301)
		foreach ((string FromDomain, string ToDomain) in RedirectOptions.HostMappings)
		{
			if (Host.Equals(FromDomain, StringComparison.OrdinalIgnoreCase))
			{
				await RedirectToHostAsync(Context, ToDomain, StatusCodes.Status301MovedPermanently);
				return;
			}

			if (Host.EndsWith($".{FromDomain}", StringComparison.OrdinalIgnoreCase))
			{
				string Subdomain = Host[..^FromDomain.Length];
				await RedirectToHostAsync(Context, $"{Subdomain}{ToDomain}", StatusCodes.Status301MovedPermanently);
				return;
			}
		}

		// 3. Database-driven article domain redirects (308)
		string? ArticleRedirectUrl = await DomainRedirectService.ResolveRedirectUrlAsync(
			Host,
			Context.Request,
			Context.RequestAborted);

		if (!string.IsNullOrEmpty(ArticleRedirectUrl))
		{
			Context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
			Context.Response.Headers.Location = ArticleRedirectUrl;
			return;
		}

		// 4. Unknown hosts go to the configured target (308)
		if (!string.IsNullOrWhiteSpace(RedirectOptions.UnknownHostTarget) &&
			!IsKnownHost(Host) &&
			!Host.Equals(RedirectOptions.UnknownHostTarget, StringComparison.OrdinalIgnoreCase))
		{
			await RedirectToHostAsync(Context, RedirectOptions.UnknownHostTarget, StatusCodes.Status308PermanentRedirect);
			return;
		}

		// 5. Root path on a culture-selector root domain renders the culture selector
		if ((!Context.Request.Path.HasValue || Context.Request.Path == "/") &&
			IsCultureSelectorRootDomain(Host))
		{
			Context.Request.Path = "/CultureSelect";
		}

		await Next(Context);
	}

	private static Task RedirectToHostAsync(HttpContext Context, string NewHost, int StatusCode)
	{
		HttpRequest Request = Context.Request;
		HostString Target = Request.Host.Port is int Port
			? new HostString(NewHost, Port)
			: new HostString(NewHost);

		string Location = UriHelper.BuildAbsolute(
			Request.Scheme,
			Target,
			Request.PathBase,
			Request.Path,
			Request.QueryString);

		Context.Response.StatusCode = StatusCode;
		Context.Response.Headers.Location = Location;
		return Task.CompletedTask;
	}

	private bool IsKnownHost(string Host)
	{
		return KnownHosts.Contains(Host) ||
			Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
			Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Determines whether the host is the root domain of a site configured to only serve
	/// a culture selector on its root domain. Mirrors the domain matching semantics of
	/// <see cref="Services.SiteResolverService"/>: the base domain is the first configured
	/// domain the host equals or is a subdomain of, and the host is "root" when it equals
	/// that base domain exactly.
	/// </summary>
	private bool IsCultureSelectorRootDomain(string Host)
	{
		foreach (SiteInfo Site in SiteConfiguration.Sites)
		{
			foreach (string Domain in Site.Domains)
			{
				if (Host.Equals(Domain, StringComparison.OrdinalIgnoreCase))
				{
					return Site.RootDomainIsCultureSelectorOnly;
				}

				if (Host.EndsWith($".{Domain}", StringComparison.OrdinalIgnoreCase))
				{
					// Host is a subdomain of this base domain (e.g. a culture subdomain), not a root domain.
					return false;
				}
			}
		}

		return false;
	}
}
