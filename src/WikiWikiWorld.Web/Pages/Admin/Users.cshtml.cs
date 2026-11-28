using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Infrastructure;

namespace WikiWikiWorld.Web.Pages.Admin;

/// <summary>
/// Displays and manages the shared user directory for administrators.
/// </summary>
/// <param name="UserAdministration">The service for browsing accounts and changing their access.</param>
/// <param name="SiteResolverService">The service that resolves the current site context.</param>
/// <param name="FileStorage">The configured root of public site files.</param>
/// <param name="SiteConfiguration">The sites that can store a profile photo for a shared account.</param>
[Authorize(Policy = nameof(Permission.UseAdminTools))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsersModel(UserAdministrationService UserAdministration, SiteResolverService SiteResolverService,
	IOptions<FileStorageOptions> FileStorage, IOptions<SiteConfiguration> SiteConfiguration)
	: BasePageModel(SiteResolverService)
{
	/// <summary>Gets or sets the username filter.</summary>
	[BindProperty(SupportsGet = true)]
	public string? Search { get; set; }

	/// <summary>Gets or sets the optional administrative access status filter.</summary>
	[BindProperty(SupportsGet = true)]
	public bool? Disabled { get; set; }

	/// <summary>Gets or sets the optional role filter.</summary>
	[BindProperty(SupportsGet = true)]
	public Guid? Role { get; set; }

	/// <summary>Gets or sets the sort selection, defaulting to newest registration first.</summary>
	[BindProperty(SupportsGet = true)]
	public bool Oldest { get; set; }

	/// <summary>Gets or sets the requested page number.</summary>
	[BindProperty(SupportsGet = true)]
	public int PageNumber { get; set; } = 1;

	/// <summary>Gets the account results for the current request.</summary>
	public RegisteredUsersPage Results { get; private set; } = new([], [], 0, 1, 1);

	/// <summary>Gets the signed-in administrator's account identifier.</summary>
	public Guid? CurrentAccountId => PermissionService.GetUserId(User);

	/// <summary>Finds a shared account's photo in the existing per-site profile folders, preferring the current site.</summary>
	/// <param name="ProfilePicGuid">The stored profile photo identifier, if any.</param>
	/// <returns>The public photo URL, or null so the directory can show an initial.</returns>
	public string? GetAvatarUrl(Guid? ProfilePicGuid)
	{
		if (ProfilePicGuid is not Guid Picture) return null;

		string FileName = $"{Picture}.png";
		foreach (SiteInfo Site in SiteConfiguration.Value.Sites.OrderByDescending(Site => Site.SiteId == SiteId))
		{
			string Partition = Site.SiteId.ToString(CultureInfo.InvariantCulture);
			if (System.IO.File.Exists(Path.Combine(FileStorage.Value.SiteFilesPath, Partition, "profilepics", FileName)))
			{
				return Url.Content($"~/sitefiles/{Partition}/profilepics/{FileName}");
			}
		}

		return null;
	}

	/// <summary>Gets or sets the status notice carried across a redirect.</summary>
	[TempData]
	public string? StatusMessage { get; set; }

	/// <summary>Gets or sets the error notice carried across a redirect.</summary>
	[TempData]
	public string? ErrorMessage { get; set; }

	/// <summary>Loads the current page of registered accounts.</summary>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>A task that completes when the directory is ready to render.</returns>
	public async Task OnGetAsync(CancellationToken CancellationToken)
	{
		AllowSearchEngineIndexingOfPage = false;
		Results = await UserAdministration.FindUsersAsync(Search, Disabled, Role, Oldest, PageNumber, CancellationToken);
	}

	/// <summary>Applies an explicit enable or disable request and returns to the current filters.</summary>
	/// <param name="AccountId">The account to change.</param>
	/// <param name="SetDisabled">The desired access state.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>A redirect with the action result, or a bad request for invalid values.</returns>
	public async Task<IActionResult> OnPostAsync(Guid AccountId, bool? SetDisabled, CancellationToken CancellationToken)
	{
		if (!ModelState.IsValid || AccountId == Guid.Empty || SetDisabled is null)
			return BadRequest();

		if (SetDisabled.Value && AccountId == CurrentAccountId)
		{
			ErrorMessage = SharedResource.Text("You cannot disable your own account.");
		}
		else
		{
			string? UserName = await UserAdministration.SetDisabledAsync(AccountId, SetDisabled.Value, CancellationToken);
			if (UserName is null)
			{
				ErrorMessage = SharedResource.Text("The account is unavailable or was changed. Refresh the directory and try again.");
			}
			else
			{
				StatusMessage = SetDisabled.Value
					? SharedResource.Format("Disabled account {0}.", UserName)
					: SharedResource.Format("Enabled account {0}.", UserName);
			}
		}

		return RedirectToPage(new
		{
			search = Search,
			disabled = Disabled,
			role = Role,
			oldest = Oldest ? true : (bool?)null,
			pagenumber = PageNumber > 1 ? PageNumber : (int?)null
		});
	}
}
