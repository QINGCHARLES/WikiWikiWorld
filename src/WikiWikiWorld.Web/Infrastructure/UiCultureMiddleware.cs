namespace WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// Sets <see cref="CultureInfo.CurrentUICulture"/> for the request from the site culture in its host name, so
/// shared UI text (<see cref="SharedResource"/>) renders in the page's language.
/// </summary>
/// <remarks>
/// Only the UI culture changes. <see cref="CultureInfo.CurrentCulture"/>, which drives number and date
/// formatting, is left as it was, because some cultures (Arabic in particular) bring a non-Gregorian calendar
/// and digit conventions the site has not chosen. Hosts that resolve to no site or culture (redirect sources,
/// the culture-selector root) and codes that are not real cultures (such as <c>xx-test</c>) get English.
/// </remarks>
/// <param name="Next">The next middleware in the pipeline.</param>
public sealed class UiCultureMiddleware(RequestDelegate Next)
{
	/// <summary>
	/// The culture used when the request has no usable site culture.
	/// </summary>
	private static readonly CultureInfo Fallback = CultureInfo.GetCultureInfo("en");

	/// <summary>
	/// Resolves the request's UI culture, sets it for the rest of the pipeline and restores the previous value.
	/// </summary>
	/// <param name="Context">The current request.</param>
	/// <param name="SiteResolver">Resolves the site and culture from the host name.</param>
	/// <returns>A task that completes when the rest of the pipeline has handled the request.</returns>
	public async Task InvokeAsync(HttpContext Context, SiteResolverService SiteResolver)
	{
		CultureInfo Previous = CultureInfo.CurrentUICulture;
		CultureInfo.CurrentUICulture = Resolve(SiteResolver);

		try
		{
			await Next(Context);
		}
		finally
		{
			CultureInfo.CurrentUICulture = Previous;
		}
	}

	/// <summary>
	/// Maps the request's site culture to a UI culture.
	/// </summary>
	/// <param name="SiteResolver">Resolves the site and culture from the host name.</param>
	/// <returns>The culture for the host's language, or English when there is none or it is not a known culture.</returns>
	private static CultureInfo Resolve(SiteResolverService SiteResolver)
	{
		string Culture;
		try
		{
			(_, Culture, _) = SiteResolver.ResolveSiteAndCultureWithRootCheck();
		}
		catch (InvalidOperationException)
		{
			return Fallback;
		}

		if (string.IsNullOrWhiteSpace(Culture))
			return Fallback;

		try
		{
			CultureInfo Candidate = CultureInfo.GetCultureInfo(Culture);
			return Candidate.CultureTypes.HasFlag(CultureTypes.UserCustomCulture) ? Fallback : Candidate;
		}
		catch (CultureNotFoundException)
		{
			return Fallback;
		}
	}
}
