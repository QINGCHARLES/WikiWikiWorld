using System.Net;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// Canonicalizes the request host before routing: strips "www.", applies configured host
/// mappings (for example, magazedia.wiki to magazedia.com), redirects unknown hosts to a
/// configured target, answers remaining hosts that match no configured site with a standalone
/// 404 page, and rewrites the root path to the culture selector page for sites
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
	/// <remarks>
	/// Redirect source hosts (www hosts, mapped domains, article redirect domains, unknown hosts) are not
	/// configured sites, so nothing on this path may resolve the request-scoped
	/// <see cref="WikiWikiWorldDbContext"/>: its constructor resolves the site from the host and throws
	/// for those hosts. The configuration-driven redirects run without any database access, and the
	/// article redirect lookup uses its own cross-site context.
	/// </remarks>
	/// <param name="Context">The HTTP context.</param>
	/// <returns>A task that completes when the request has been handled.</returns>
	public async Task InvokeAsync(HttpContext Context)
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

		// 2. Host suffix mappings, preserving any subdomain when the target is not itself under the source domain (301)
		if (!KnownHosts.Contains(Host))
		{
			foreach ((string FromDomain, string ToDomain) in RedirectOptions.HostMappings)
			{
				if (Host.Equals(FromDomain, StringComparison.OrdinalIgnoreCase))
				{
					await RedirectToHostAsync(Context, ToDomain, StatusCodes.Status301MovedPermanently);
					return;
				}

				if (!ToDomain.EndsWith($".{FromDomain}", StringComparison.OrdinalIgnoreCase) &&
					Host.EndsWith($".{FromDomain}", StringComparison.OrdinalIgnoreCase))
				{
					string Subdomain = Host[..^FromDomain.Length];
					await RedirectToHostAsync(Context, $"{Subdomain}{ToDomain}", StatusCodes.Status301MovedPermanently);
					return;
				}
			}
		}

		// 3. Database-driven article domain redirects (308)
		string? ArticleRedirectUrl = await ResolveArticleRedirectUrlAsync(Context, Host);

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

		// 5. Hosts that survive the redirects but match no configured site (for example bogus.localhost in
		// development) get a self-contained page; continuing would throw when the site-scoped DbContext is built.
		if (!MatchesConfiguredSite(Host))
		{
			await WriteUnknownHostPageAsync(Context, Host);
			return;
		}

		// 6. Root path on a culture-selector root domain renders the culture selector
		if ((!Context.Request.Path.HasValue || Context.Request.Path == "/") &&
			IsCultureSelectorRootDomain(Host))
		{
			Context.Request.Path = "/CultureSelect";
		}

		await Next(Context);
	}

	/// <summary>
	/// Looks up a database-driven article domain redirect for the host using a dedicated cross-site
	/// <see cref="WikiWikiWorldDbContext"/>, so the lookup works for hosts that are not configured sites.
	/// </summary>
	/// <remarks>
	/// The context is built from the registered options (keeping the SQLite interceptors) with the
	/// options-only constructor, which skips per-request site resolution. Every query in
	/// <see cref="ArticleDomainRedirectService.ResolveRedirectUrlAsync"/> already ignores the site and
	/// culture filters, so the result matches the request-scoped service. The redirect map cache is the
	/// shared <see cref="IMemoryCache"/> entry, so invalidation by the admin page still applies.
	/// </remarks>
	/// <param name="Context">The HTTP context whose request services and request are used.</param>
	/// <param name="Host">The request host to look up.</param>
	/// <returns>The absolute redirect URL, or <see langword="null"/> when the host has no article redirect.</returns>
	private static async Task<string?> ResolveArticleRedirectUrlAsync(HttpContext Context, string Host)
	{
		IServiceProvider RequestServices = Context.RequestServices;

		await using WikiWikiWorldDbContext CrossSiteDbContext = new(
			RequestServices.GetRequiredService<DbContextOptions<WikiWikiWorldDbContext>>());

		ArticleDomainRedirectService DomainRedirectService = new(
			CrossSiteDbContext,
			RequestServices.GetRequiredService<IMemoryCache>(),
			RequestServices.GetRequiredService<IOptions<SiteConfiguration>>(),
			RequestServices.GetRequiredService<IOptions<DomainRedirectOptions>>());

		return await DomainRedirectService.ResolveRedirectUrlAsync(
			Host,
			Context.Request,
			Context.RequestAborted);
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

	/// <summary>
	/// Determines whether the host equals, or is a subdomain of, any configured site domain: the same rule
	/// <see cref="Services.SiteResolverService"/> uses to find the base domain, so a match here means site
	/// resolution will not fail for want of a domain.
	/// </summary>
	/// <param name="Host">The request host, without port.</param>
	/// <returns><see langword="true"/> when a configured site serves the host.</returns>
	private bool MatchesConfiguredSite(string Host)
	{
		return SiteConfiguration.Sites
			.SelectMany(Site => Site.Domains)
			.Any(Domain => Host.Equals(Domain, StringComparison.OrdinalIgnoreCase) ||
				Host.EndsWith($".{Domain}", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Writes a standalone 404 page explaining that no wiki is configured for the host, linking to each
	/// configured site's first domain on the current scheme and port. It uses no layout, database or
	/// site services, because all of those require a resolvable site. The body is non-empty, so the
	/// status-code re-execution to /NotFound (which would itself need a site) does not run.
	/// </summary>
	/// <param name="Context">The HTTP context to respond on.</param>
	/// <param name="Host">The unmatched request host, shown to the reader.</param>
	/// <returns>A task that completes when the response has been written.</returns>
	private Task WriteUnknownHostPageAsync(HttpContext Context, string Host)
	{
		HttpRequest Request = Context.Request;
		string SiteLinks = string.Concat(SiteConfiguration.Sites
			.Where(Site => Site.Domains.Count > 0)
			.Select(Site =>
			{
				HostString Target = Request.Host.Port is int Port ? new HostString(Site.Domains[0], Port) : new HostString(Site.Domains[0]);
				string Url = UriHelper.BuildAbsolute(Request.Scheme, Target);
				return $"<li><a href=\"{WebUtility.HtmlEncode(Url)}\">{WebUtility.HtmlEncode(Site.SiteName)}</a> <span>{WebUtility.HtmlEncode(Target.Value)}</span></li>";
			}));

		string Html = $$"""
			<!doctype html>
			<html lang="en">
			<head>
			<meta charset="utf-8">
			<meta name="viewport" content="width=device-width, initial-scale=1">
			<meta name="robots" content="noindex">
			<title>No wiki at this address</title>
			<style>
			:root { color-scheme: light dark; font-family: system-ui, sans-serif; line-height: 1.5; }
			body { max-inline-size: 36rem; margin: 15vh auto; padding-inline: 1.5rem; }
			h1 { font-size: 1.75rem; line-height: 1.2; }
			code { padding: .1em .35em; border-radius: .3em; background: color-mix(in srgb, currentColor 10%, transparent); }
			ul { padding: 0; list-style: none; }
			li { margin-block: .6rem; }
			a { font-weight: 600; }
			span { opacity: .65; font-size: .9em; }
			</style>
			</head>
			<body>
			<h1>No wiki at this address</h1>
			<p><code>{{WebUtility.HtmlEncode(Host)}}</code> isn’t set up on this server. Try one of these instead:</p>
			<ul>{{SiteLinks}}</ul>
			</body>
			</html>
			""";

		Context.Response.StatusCode = StatusCodes.Status404NotFound;
		Context.Response.ContentType = "text/html; charset=utf-8";
		Context.Response.Headers.CacheControl = "no-store";
		return Context.Response.WriteAsync(Html, Context.RequestAborted);
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
