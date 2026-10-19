using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Services;

namespace WikiWikiWorld.Web.Pages.Admin;

/// <summary>
/// Administrator dashboard for managing domain redirects that route incoming domains to specific articles.
/// </summary>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="DomainRedirectService">The domain redirect service.</param>
/// <param name="SiteConfigurationOptions">The site configuration options.</param>
/// <param name="UserManager">The identity user manager.</param>
[Authorize]
public sealed class DomainRedirectsModel(
	SiteResolverService SiteResolverService,
	ArticleDomainRedirectService DomainRedirectService,
	IOptions<SiteConfiguration> SiteConfigurationOptions,
	UserManager<User> UserManager) : BasePageModel(SiteResolverService)
{
	private readonly SiteConfiguration SiteConfiguration = SiteConfigurationOptions.Value;

	/// <inheritdoc/>
	protected override bool AllowsCultureSelectorRootDomain => true;

	/// <summary>
	/// Gets the list of existing active domain redirects.
	/// </summary>
	public IReadOnlyList<ArticleDomainRedirectAdminDto> Redirects { get; private set; } = [];

	/// <summary>
	/// Gets the configured sites.
	/// </summary>
	public IReadOnlyList<SiteInfo> Sites => SiteConfiguration.Sites;

	/// <summary>
	/// Gets or sets the success notification message.
	/// </summary>
	[TempData]
	public string? SuccessMessage { get; set; }

	/// <summary>
	/// Gets or sets the error notification message.
	/// </summary>
	public string? ErrorMessage { get; set; }

	/// <summary>
	/// Gets or sets the domain name to redirect.
	/// </summary>
	[BindProperty]
	public string InputDomain { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the selected target site identifier.
	/// </summary>
	[BindProperty]
	public int InputSiteId { get; set; }

	/// <summary>
	/// Gets or sets the selected target culture code.
	/// </summary>
	[BindProperty]
	public string InputCulture { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the selected target canonical article identifier.
	/// </summary>
	[BindProperty]
	public Guid InputCanonicalArticleId { get; set; }

	/// <summary>
	/// Gets or sets the selected article title for redisplay if validation fails.
	/// </summary>
	[BindProperty]
	public string? SelectedArticleTitle { get; set; }

	/// <summary>
	/// Loads the domain redirects list.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The page result.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		AllowSearchEngineIndexingOfPage = false;
		Redirects = await DomainRedirectService.GetActiveRedirectsAsync(CancellationToken);

		if (InputSiteId == 0 && Sites.Count > 0)
		{
			InputSiteId = Sites[0].SiteId;
		}

		if (string.IsNullOrWhiteSpace(InputCulture) && Sites.Count > 0)
		{
			InputCulture = GetDefaultCultureForSite(Sites[0]);
		}

		return Page();
	}

	/// <summary>
	/// Handles creation of a new domain redirect.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The page result or redirect.</returns>
	public async Task<IActionResult> OnPostCreateAsync(CancellationToken CancellationToken)
	{
		AllowSearchEngineIndexingOfPage = false;

		Guid? CurrentUserId = GetCurrentUserId();
		if (CurrentUserId is null)
		{
			return Challenge();
		}

		(bool Success, string? Error) = await DomainRedirectService.CreateRedirectAsync(
			InputDomain,
			InputSiteId,
			InputCulture,
			InputCanonicalArticleId,
			CurrentUserId.Value,
			CancellationToken);

		if (!Success)
		{
			ErrorMessage = Error;
			Redirects = await DomainRedirectService.GetActiveRedirectsAsync(CancellationToken);
			return Page();
		}

		SuccessMessage = $"Domain redirect for '{ArticleDomainRedirectService.NormalizeDomain(InputDomain)}' created successfully.";
		return RedirectToPage();
	}

	/// <summary>
	/// Handles deletion of an existing domain redirect.
	/// </summary>
	/// <param name="Id">The redirect identifier.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The redirect result.</returns>
	public async Task<IActionResult> OnPostDeleteAsync(int Id, CancellationToken CancellationToken)
	{
		bool Deleted = await DomainRedirectService.DeleteRedirectAsync(Id, CancellationToken);

		if (Deleted)
		{
			SuccessMessage = "Domain redirect deleted successfully.";
		}
		else
		{
			ErrorMessage = "Failed to delete domain redirect.";
		}

		return RedirectToPage();
	}

	/// <summary>
	/// AJAX endpoint to search for current articles in a site and culture.
	/// </summary>
	/// <param name="SiteId">The site identifier.</param>
	/// <param name="Culture">The culture code.</param>
	/// <param name="Q">The search query.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A JSON list of matching articles.</returns>
	public async Task<IActionResult> OnGetSearchArticlesAsync(
		int SiteId,
		string Culture,
		string? Q,
		CancellationToken CancellationToken)
	{
		if (string.IsNullOrWhiteSpace(Q))
		{
			return new JsonResult(Array.Empty<ArticleLookupItemDto>());
		}

		IReadOnlyList<ArticleLookupItemDto> Results = await DomainRedirectService.SearchArticlesAsync(
			SiteId,
			Culture,
			Q,
			CancellationToken);

		return new JsonResult(Results);
	}

	/// <summary>
	/// AJAX endpoint to get supported cultures for a site.
	/// </summary>
	/// <param name="SiteId">The site identifier.</param>
	/// <returns>A JSON list of culture codes.</returns>
	public IActionResult OnGetCulturesAsync(int SiteId)
	{
		SiteInfo? Site = Sites.FirstOrDefault(S => S.SiteId == SiteId);
		if (Site is null)
		{
			return new JsonResult(new[] { "en" });
		}

		List<string> Cultures = GetCulturesForSite(Site);
		return new JsonResult(Cultures);
	}

	/// <summary>
	/// Gets the list of available cultures for a given site info.
	/// </summary>
	/// <param name="Site">The site info.</param>
	/// <returns>List of culture codes.</returns>
	public static List<string> GetCulturesForSite(SiteInfo Site)
	{
		HashSet<string> Cultures = [];

		if (!string.IsNullOrWhiteSpace(Site.DefaultCulture))
		{
			Cultures.Add(Site.DefaultCulture);
		}

		foreach (string Domain in Site.Domains)
		{
			int FirstDot = Domain.IndexOf('.');
			if (FirstDot > 0)
			{
				string PotentialCulture = Domain[..FirstDot];
				if (PotentialCulture != "www" && PotentialCulture != "localhost")
				{
					Cultures.Add(PotentialCulture);
				}
			}
		}

		if (Cultures.Count == 0)
		{
			Cultures.Add("en");
		}

		return [.. Cultures.OrderBy(C => C)];
	}

	private static string GetDefaultCultureForSite(SiteInfo Site)
	{
		if (!string.IsNullOrWhiteSpace(Site.DefaultCulture))
		{
			return Site.DefaultCulture;
		}

		List<string> Cultures = GetCulturesForSite(Site);
		return Cultures.Count > 0 ? Cultures[0] : "en";
	}

	private Guid? GetCurrentUserId()
	{
		string? UserIdString = UserManager.GetUserId(User);
		return Guid.TryParse(UserIdString, out Guid ParsedId) ? ParsedId : null;
	}
}
