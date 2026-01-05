using Microsoft.AspNetCore.Http;
using WikiWikiWorld.Web.Pages;

namespace WikiWikiWorld.Web.Pages.Settings
{
	/// <summary>
	/// Page model for handling theme selection (light/dark/system).
	/// </summary>
	/// <param name="SiteResolverService">The site resolver service.</param>
	[ValidateAntiForgeryToken]
	public sealed class ThemeModel(SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
	{
		/// <summary>
		/// Gets or sets the selected theme.
		/// </summary>
		[BindProperty]
		public string? Theme { get; set; }

		/// <summary>
		/// Handles the GET request for the theme settings page.
		/// </summary>
		public void OnGet()
		{
			// ThemePageFilter seeds ViewData with the resolved preference.
		}

		/// <summary>
		/// Handles the POST request to save the theme preference.
		/// </summary>
		/// <returns>A redirect to the previous page or home.</returns>
		public IActionResult OnPost()
		{
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
				Secure = HttpContext.Request.IsHttps
			};

			Response.Cookies.Append("theme", Normalized, Options);
			HttpContext.Items["UserProfileTheme"] = Normalized;

			string RefererHeader = Request.Headers["Referer"].ToString();
			if (!string.IsNullOrWhiteSpace(RefererHeader))
			{
				if (Uri.TryCreate(RefererHeader, UriKind.Absolute, out Uri? RefererUri))
				{
					if (string.Equals(RefererUri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
					{
						string Target = string.Concat(RefererUri.PathAndQuery, RefererUri.Fragment);
						return Redirect(string.IsNullOrEmpty(Target) ? "/" : Target);
					}
				}
				else if (Uri.TryCreate(RefererHeader, UriKind.Relative, out Uri? _))
				{
					return Redirect(RefererHeader);
				}
			}

			return RedirectToPage("/Index");
		}
	}
}
