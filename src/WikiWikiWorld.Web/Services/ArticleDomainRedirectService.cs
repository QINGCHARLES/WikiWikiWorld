using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Helpers;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Service to manage domain redirects to canonical articles and resolve incoming host requests.
/// </summary>
public sealed class ArticleDomainRedirectService
{
	private const string CacheKey = "ArticleDomainRedirects_LookupMap";
	private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

	private readonly WikiWikiWorldDbContext DbContext;
	private readonly IMemoryCache MemoryCache;
	private readonly SiteConfiguration SiteConfiguration;
	private readonly DomainRedirectOptions DomainRedirectOptions;

	/// <summary>
	/// Initializes a new instance of the <see cref="ArticleDomainRedirectService"/> class.
	/// </summary>
	/// <param name="DbContext">The database context.</param>
	/// <param name="MemoryCache">The memory cache.</param>
	/// <param name="SiteConfigurationOptions">The site configuration options.</param>
	/// <param name="DomainRedirectOptions">The domain redirect options.</param>
	public ArticleDomainRedirectService(
		WikiWikiWorldDbContext DbContext,
		IMemoryCache MemoryCache,
		IOptions<SiteConfiguration> SiteConfigurationOptions,
		IOptions<DomainRedirectOptions> DomainRedirectOptions)
	{
		this.DbContext = DbContext;
		this.MemoryCache = MemoryCache;
		this.SiteConfiguration = SiteConfigurationOptions.Value;
		this.DomainRedirectOptions = DomainRedirectOptions.Value;
	}

	/// <summary>
	/// Resolves an incoming host to a target canonical article redirect URL, if a mapping exists.
	/// </summary>
	/// <param name="IncomingHost">The incoming host from the request.</param>
	/// <param name="Request">The current HTTP request.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The target redirect URL, or <see langword="null"/> if no redirect is configured.</returns>
	public async Task<string?> ResolveRedirectUrlAsync(
		string IncomingHost,
		HttpRequest Request,
		CancellationToken CancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(IncomingHost))
		{
			return null;
		}

		string NormalizedHost = NormalizeDomain(IncomingHost);

		IReadOnlyDictionary<string, DomainMappingTarget> LookupMap = await GetOrLoadLookupMapAsync(CancellationToken);

		if (!LookupMap.TryGetValue(NormalizedHost, out DomainMappingTarget? Target))
		{
			// Check if host had www and try without www
			if (NormalizedHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && NormalizedHost.Length > 4)
			{
				string WithoutWww = NormalizedHost[4..];
				if (!LookupMap.TryGetValue(WithoutWww, out Target))
				{
					return null;
				}
			}
			else
			{
				return null;
			}
		}

		// Look up the current article revision for this canonical ID, culture, and site
		ArticleRevision? CurrentRevision = await DbContext.ArticleRevisions
			.AsNoTracking()
			.IgnoreQueryFilters([WikiWikiWorldDbContext.SiteFilter, WikiWikiWorldDbContext.CultureFilter])
			.Where(Revision => Revision.SiteId == Target.SiteId &&
				Revision.Culture == Target.Culture &&
				Revision.CanonicalArticleId == Target.CanonicalArticleId &&
				Revision.IsCurrent)
			.FirstOrDefaultAsync(CancellationToken);

		SiteInfo? TargetSite = SiteConfiguration.Sites.FirstOrDefault(Site => Site.SiteId == Target.SiteId);
		string DestinationHost = ResolveDestinationHost(TargetSite, Target.Culture);

		string ArticlePath = CurrentRevision is not null
			? ArticleUrlHelper.BuildArticlePath(CurrentRevision.UrlSlug, CurrentRevision.Type)
			: "/";

		HostString TargetHost = Request.Host.Port is int Port
			? new HostString(DestinationHost, Port)
			: new HostString(DestinationHost);

		return UriHelper.BuildAbsolute(
			Request.Scheme,
			TargetHost,
			Request.PathBase,
			ArticlePath,
			Request.QueryString);
	}

	/// <summary>
	/// Gets all active domain redirects with article information for administration.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A read-only list of active domain redirects.</returns>
	public async Task<IReadOnlyList<ArticleDomainRedirectAdminDto>> GetActiveRedirectsAsync(CancellationToken CancellationToken = default)
	{
		List<ArticleDomainRedirect> Redirects = await DbContext.ArticleDomainRedirects
			.AsNoTracking()
			.IgnoreQueryFilters([WikiWikiWorldDbContext.SiteFilter, WikiWikiWorldDbContext.CultureFilter])
			.OrderBy(Redirect => Redirect.Domain)
			.ToListAsync(CancellationToken);

		if (Redirects.Count == 0)
		{
			return [];
		}

		List<Guid> CanonicalIds = [.. Redirects.Select(Redirect => Redirect.CanonicalArticleId).Distinct()];

		List<ArticleRevision> Revisions = await DbContext.ArticleRevisions
			.AsNoTracking()
			.IgnoreQueryFilters([WikiWikiWorldDbContext.SiteFilter, WikiWikiWorldDbContext.CultureFilter])
			.Where(Revision => CanonicalIds.Contains(Revision.CanonicalArticleId) && Revision.IsCurrent)
			.ToListAsync(CancellationToken);

		Dictionary<(int SiteId, string Culture, Guid CanonicalArticleId), ArticleRevision> RevisionMap =
			Revisions.ToDictionary(Revision => (Revision.SiteId, Revision.Culture, Revision.CanonicalArticleId));

		Dictionary<int, string> SiteNameMap = SiteConfiguration.Sites
			.ToDictionary(Site => Site.SiteId, Site => Site.SiteName);

		List<ArticleDomainRedirectAdminDto> Result = [];

		foreach (ArticleDomainRedirect Redirect in Redirects)
		{
			SiteNameMap.TryGetValue(Redirect.SiteId, out string? SiteName);
			RevisionMap.TryGetValue((Redirect.SiteId, Redirect.Culture, Redirect.CanonicalArticleId), out ArticleRevision? Revision);

			Result.Add(new ArticleDomainRedirectAdminDto
			{
				Id = Redirect.Id,
				Domain = Redirect.Domain,
				SiteId = Redirect.SiteId,
				SiteName = SiteName ?? $"Site #{Redirect.SiteId}",
				Culture = Redirect.Culture,
				CanonicalArticleId = Redirect.CanonicalArticleId,
				ArticleTitle = Revision?.Title ?? "(Article revision not found)",
				ArticleUrlSlug = Revision?.UrlSlug ?? string.Empty,
				ArticleType = Revision?.Type ?? ArticleType.Article,
				DateCreated = Redirect.DateCreated
			});
		}

		return Result;
	}

	/// <summary>
	/// Creates a new domain redirect mapping.
	/// </summary>
	/// <param name="Domain">The incoming domain to redirect.</param>
	/// <param name="SiteId">The target site identifier.</param>
	/// <param name="Culture">The target culture.</param>
	/// <param name="CanonicalArticleId">The target canonical article identifier.</param>
	/// <param name="UserId">The user creating the redirect.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A result indicating success or the validation error message.</returns>
	public async Task<(bool Success, string? ErrorMessage)> CreateRedirectAsync(
		string Domain,
		int SiteId,
		string Culture,
		Guid CanonicalArticleId,
		Guid UserId,
		CancellationToken CancellationToken = default)
	{
		string NormalizedDomain = NormalizeDomain(Domain);

		if (string.IsNullOrWhiteSpace(NormalizedDomain))
		{
			return (false, "Domain is required.");
		}

		if (NormalizedDomain.Contains('/') || NormalizedDomain.Contains('?') || NormalizedDomain.Contains('#'))
		{
			return (false, "Domain must not contain paths, query parameters, or URL fragments.");
		}

		if (string.IsNullOrWhiteSpace(Culture))
		{
			return (false, "Culture is required.");
		}

		if (CanonicalArticleId == Guid.Empty)
		{
			return (false, "A target article must be selected.");
		}

		// Check if domain is already mapped
		bool Exists = await DbContext.ArticleDomainRedirects
			.IgnoreQueryFilters([WikiWikiWorldDbContext.SiteFilter, WikiWikiWorldDbContext.CultureFilter])
			.AnyAsync(Redirect => Redirect.Domain == NormalizedDomain, CancellationToken);

		if (Exists)
		{
			return (false, $"Domain '{NormalizedDomain}' is already configured as a redirect.");
		}

		// Verify target article exists
		bool ArticleExists = await DbContext.ArticleRevisions
			.IgnoreQueryFilters([WikiWikiWorldDbContext.SiteFilter, WikiWikiWorldDbContext.CultureFilter])
			.AnyAsync(Revision => Revision.SiteId == SiteId &&
				Revision.Culture == Culture &&
				Revision.CanonicalArticleId == CanonicalArticleId &&
				Revision.IsCurrent, CancellationToken);

		if (!ArticleExists)
		{
			return (false, "Selected article could not be found for the specified site and culture.");
		}

		ArticleDomainRedirect NewRedirect = new()
		{
			Domain = NormalizedDomain,
			SiteId = SiteId,
			Culture = Culture,
			CanonicalArticleId = CanonicalArticleId,
			CreatedByUserId = UserId,
			DateCreated = DateTimeOffset.UtcNow
		};

		DbContext.ArticleDomainRedirects.Add(NewRedirect);
		await DbContext.SaveChangesAsync(CancellationToken);

		InvalidateCache();
		return (true, null);
	}

	/// <summary>
	/// Deletes a domain redirect by ID (soft delete).
	/// </summary>
	/// <param name="Id">The redirect identifier.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns><see langword="true"/> if deleted; otherwise, <see langword="false"/>.</returns>
	public async Task<bool> DeleteRedirectAsync(int Id, CancellationToken CancellationToken = default)
	{
		ArticleDomainRedirect? Redirect = await DbContext.ArticleDomainRedirects
			.IgnoreQueryFilters([WikiWikiWorldDbContext.SiteFilter, WikiWikiWorldDbContext.CultureFilter])
			.FirstOrDefaultAsync(Item => Item.Id == Id, CancellationToken);

		if (Redirect is null)
		{
			return false;
		}

		Redirect.DateDeleted = DateTimeOffset.UtcNow;
		await DbContext.SaveChangesAsync(CancellationToken);

		InvalidateCache();
		return true;
	}

	/// <summary>
	/// Searches for current articles matching a query within a site and culture to assist in linking.
	/// </summary>
	/// <param name="SiteId">The site identifier.</param>
	/// <param name="Culture">The culture code.</param>
	/// <param name="Query">The search query (title or slug).</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A read-only list of matching articles.</returns>
	public async Task<IReadOnlyList<ArticleLookupItemDto>> SearchArticlesAsync(
		int SiteId,
		string Culture,
		string Query,
		CancellationToken CancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(Query))
		{
			return [];
		}

		string LikePattern = $"%{Query.Trim()}%";

		List<ArticleRevision> Matches = await DbContext.ArticleRevisions
			.AsNoTracking()
			.IgnoreQueryFilters([WikiWikiWorldDbContext.SiteFilter, WikiWikiWorldDbContext.CultureFilter])
			.Where(Revision => Revision.SiteId == SiteId &&
				Revision.Culture == Culture &&
				Revision.IsCurrent &&
				(EF.Functions.Like(Revision.Title, LikePattern) || EF.Functions.Like(Revision.UrlSlug, LikePattern)))
			.OrderBy(Revision => Revision.Title)
			.Take(20)
			.ToListAsync(CancellationToken);

		return [.. Matches.Select(Revision => new ArticleLookupItemDto(
			Revision.CanonicalArticleId,
			Revision.Title,
			Revision.UrlSlug,
			Revision.Type))];
	}

	/// <summary>
	/// Normalizes a domain name by stripping scheme, port, trailing slashes, and converting to lowercase.
	/// </summary>
	/// <param name="Domain">The raw domain string.</param>
	/// <returns>The normalized domain string.</returns>
	public static string NormalizeDomain(string Domain)
	{
		if (string.IsNullOrWhiteSpace(Domain))
		{
			return string.Empty;
		}

		string Clean = Domain.Trim();

		// Strip scheme if present
		if (Clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
		{
			Clean = Clean[7..];
		}
		else if (Clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			Clean = Clean[8..];
		}

		// Strip port if present
		int ColonIndex = Clean.IndexOf(':');
		if (ColonIndex >= 0)
		{
			Clean = Clean[..ColonIndex];
		}

		// Strip trailing slash
		Clean = Clean.TrimEnd('/');

		return Clean.ToLowerInvariant();
	}

	private async Task<IReadOnlyDictionary<string, DomainMappingTarget>> GetOrLoadLookupMapAsync(CancellationToken CancellationToken)
	{
		if (MemoryCache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, DomainMappingTarget>? Cached) && Cached is not null)
		{
			return Cached;
		}

		List<ArticleDomainRedirect> Redirects = await DbContext.ArticleDomainRedirects
			.AsNoTracking()
			.IgnoreQueryFilters([WikiWikiWorldDbContext.SiteFilter, WikiWikiWorldDbContext.CultureFilter])
			.ToListAsync(CancellationToken);

		Dictionary<string, DomainMappingTarget> Map = new(StringComparer.OrdinalIgnoreCase);

		foreach (ArticleDomainRedirect Redirect in Redirects)
		{
			Map[Redirect.Domain] = new DomainMappingTarget(
				Redirect.SiteId,
				Redirect.Culture,
				Redirect.CanonicalArticleId);
		}

		MemoryCacheEntryOptions Options = new()
		{
			AbsoluteExpirationRelativeToNow = CacheDuration,
			Priority = CacheItemPriority.High
		};

		MemoryCache.Set(CacheKey, (IReadOnlyDictionary<string, DomainMappingTarget>)Map, Options);
		return Map;
	}

	private void InvalidateCache()
	{
		MemoryCache.Remove(CacheKey);
	}

	private static string ResolveDestinationHost(SiteInfo? Site, string Culture)
	{
		if (Site is null || Site.Domains.Count == 0)
		{
			return "localhost";
		}

		// If site uses culture subdomains (e.g. en.magazedia.com, en.localhost)
		if (Site.RootDomainIsCultureSelectorOnly)
		{
			string SubdomainPrefix = $"{Culture}.";
			string? MatchingDomain = Site.Domains.FirstOrDefault(Domain =>
				Domain.StartsWith(SubdomainPrefix, StringComparison.OrdinalIgnoreCase));

			if (!string.IsNullOrEmpty(MatchingDomain))
			{
				return MatchingDomain;
			}

			// Construct culture subdomain from the root domain
			string BaseDomain = Site.Domains[0];
			return $"{Culture}.{BaseDomain}";
		}

		// For single-culture sites, use the primary configured domain
		return Site.Domains[0];
	}
}

/// <summary>
/// Internal record holding the target parameters for a domain redirect.
/// </summary>
public sealed record DomainMappingTarget(int SiteId, string Culture, Guid CanonicalArticleId);

/// <summary>
/// DTO representing an article domain redirect for admin dashboard display.
/// </summary>
public sealed record ArticleDomainRedirectAdminDto
{
	/// <summary>
	/// Gets the redirect identifier.
	/// </summary>
	public required int Id { get; init; }

	/// <summary>
	/// Gets the incoming domain.
	/// </summary>
	public required string Domain { get; init; }

	/// <summary>
	/// Gets the target site identifier.
	/// </summary>
	public required int SiteId { get; init; }

	/// <summary>
	/// Gets the target site display name.
	/// </summary>
	public required string SiteName { get; init; }

	/// <summary>
	/// Gets the target culture code.
	/// </summary>
	public required string Culture { get; init; }

	/// <summary>
	/// Gets the canonical article identifier.
	/// </summary>
	public required Guid CanonicalArticleId { get; init; }

	/// <summary>
	/// Gets the title of the target article.
	/// </summary>
	public required string ArticleTitle { get; init; }

	/// <summary>
	/// Gets the current URL slug of the target article.
	/// </summary>
	public required string ArticleUrlSlug { get; init; }

	/// <summary>
	/// Gets the article type.
	/// </summary>
	public required ArticleType ArticleType { get; init; }

	/// <summary>
	/// Gets the date and time when this redirect was created.
	/// </summary>
	public required DateTimeOffset DateCreated { get; init; }
}

/// <summary>
/// DTO representing an article lookup item for the admin picker.
/// </summary>
public sealed record ArticleLookupItemDto(Guid CanonicalArticleId, string Title, string UrlSlug, ArticleType Type);
