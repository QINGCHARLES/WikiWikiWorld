using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// A page filter that resolves the user's preferred theme (light/dark/system).
/// </summary>
public sealed class ThemePageFilter : IAsyncPageFilter
{
	/// <summary>
	/// Validates whether the given value is a valid theme string.
	/// </summary>
	/// <param name="Value">The theme value to validate.</param>
	/// <returns>True if the value is "light", "dark", or "system"; otherwise, false.</returns>
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

	/// <summary>
	/// Resolves the user's preferred theme from cookies or profile.
	/// </summary>
	/// <param name="HttpContext">The HTTP context containing request data.</param>
	/// <returns>The preferred theme ("light", "dark", or "system").</returns>
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
