namespace WikiWikiWorld.Web.Pages.Settings;

/// <summary>Provides owner-only profile settings and the legacy anonymous device-theme preference.</summary>
/// <param name="SiteResolverService">Resolves the current site and culture.</param>
/// <param name="ProfileService">Loads the shared profile identity and ownership information.</param>
[ValidateAntiForgeryToken]
public sealed class ThemeModel(SiteResolverService SiteResolverService, ProfileService ProfileService) : BasePageModel(SiteResolverService)
{
	/// <summary>Gets or sets the requested profile; absent on legacy device-theme URLs.</summary>
	[BindProperty(SupportsGet = true)]
	public string? Username { get; set; }

	/// <summary>Gets or sets the submitted device preference; unknown values use the system theme.</summary>
	[BindProperty]
	public string? Theme { get; set; }

	/// <summary>Gets the shared profile header, or null for anonymous device settings.</summary>
	public ProfileHeader? Header { get; private set; }

	/// <summary>Loads the owner's settings or redirects signed-in legacy visitors to their profile URL.</summary>
	/// <param name="CancellationToken">Cancels profile loading when the request ends.</param>
	/// <returns>The settings page, canonical redirect, or an access failure.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		if (string.IsNullOrEmpty(Username) && User.Identity?.IsAuthenticated == true)
			return LocalRedirect($"/@{Uri.EscapeDataString(User.Identity.Name ?? string.Empty)}/settings");

		IActionResult? Failure = await LoadHeaderAsync(CancellationToken);
		return Failure ?? Page();
	}

	/// <summary>Saves the device theme after checking ownership and returns to settings.</summary>
	/// <param name="CancellationToken">Cancels ownership verification when the request ends.</param>
	/// <returns>The settings redirect or an access failure before any cookie is written.</returns>
	public async Task<IActionResult> OnPostAsync(CancellationToken CancellationToken)
	{
		IActionResult? Failure = await LoadHeaderAsync(CancellationToken);
		if (Failure is not null)
			return Failure;

		string Normalized = Theme?.ToLowerInvariant() switch
		{
			"light" => "light",
			"dark" => "dark",
			_ => "system"
		};
		CookieOptions Options = new()
		{
			Expires = DateTimeOffset.UtcNow.AddYears(2),
			HttpOnly = false,
			SameSite = SameSiteMode.Lax,
			Secure = Request.IsHttps,
			Path = "/"
		};
		Response.Cookies.Append("theme", Normalized, Options);
		HttpContext.Items["UserProfileTheme"] = Normalized;
		TempData["ThemeSaved"] = true;
		string? OwnerName = Header?.UserName ?? (User.Identity?.IsAuthenticated == true ? User.Identity.Name : null);
		return LocalRedirect(OwnerName is null ? "/settings/theme:" : $"/@{Uri.EscapeDataString(OwnerName)}/settings");
	}

	/// <summary>Checks profile ownership for reads and writes, retaining anonymous legacy theme access.</summary>
	/// <param name="CancellationToken">Cancels database queries for the profile header.</param>
	/// <returns>Null when allowed; otherwise a challenge, forbidden result, or missing-profile result.</returns>
	private async Task<IActionResult?> LoadHeaderAsync(CancellationToken CancellationToken)
	{
		AllowSearchEngineIndexingOfPage = false;
		if (string.IsNullOrEmpty(Username))
			return null;
		if (User.Identity?.IsAuthenticated != true)
			return Challenge();

		ProfileHeader? LoadedHeader = await ProfileService.GetHeaderAsync(Username, User, SiteId, CancellationToken);
		if (LoadedHeader is null)
			return NotFound();
		if (!LoadedHeader.IsOwner)
			return Forbid();

		Header = LoadedHeader;
		return null;
	}
}
