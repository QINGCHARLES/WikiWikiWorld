using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// A page filter that resolves the user's preferred theme (light/dark/system).
/// </summary>
public sealed class ThemePageFilter : IAsyncPageFilter
{
	private static bool IsValidTheme(string? Value)
		=> Value is "light" or "dark" or "system";

	/// <inheritdoc/>
	public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext PageHandlerSelectedContext)
		=> Task.CompletedTask;

	/// <inheritdoc/>
	public Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext PageHandlerExecutingContext, PageHandlerExecutionDelegate Next)
	{
		string Preferred = ResolvePreferredTheme(PageHandlerExecutingContext.HttpContext);
		if (PageHandlerExecutingContext.HandlerInstance is PageModel PageModel)
		{
			PageModel.ViewData["PreferredTheme"] = Preferred;
		}

		return Next();
	}

	private static string ResolvePreferredTheme(HttpContext HttpContext)
	{
		if (HttpContext.Request.Cookies.TryGetValue("theme", out string? CookieTheme) && IsValidTheme(CookieTheme))
		{
			return CookieTheme;
		}

		if (HttpContext.Items.TryGetValue("UserProfileTheme", out object? ProfileTheme) && ProfileTheme is string ProfileValue && IsValidTheme(ProfileValue))
		{
			return ProfileValue;
		}

		return "system";
	}
}
