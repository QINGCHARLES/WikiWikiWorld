using Microsoft.AspNetCore.Http;
using WikiWikiWorld.Web.Pages;

namespace WikiWikiWorld.Web.Pages.Settings
{
	[ValidateAntiForgeryToken]
	public sealed class ThemeModel(SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
	{
		[BindProperty]
		public string? Theme { get; set; }

		public void OnGet()
		{
			// ThemePageFilter seeds ViewData with the resolved preference.
		}

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
