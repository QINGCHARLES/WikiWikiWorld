namespace WikiWikiWorld.Web.Pages.Info;

/// <summary>
/// Page model for the HTML sitemap hub at <c>/info:sitemap</c>.
/// </summary>
/// <param name="SitemapService">Generates the cached HTML sitemap hub summary.</param>
/// <param name="SiteResolverService">Resolves the current site and culture.</param>
public sealed class SitemapModel(
	ISitemapService SitemapService,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// Gets the loaded HTML sitemap hub data.
	/// </summary>
	public HtmlSitemapHub Hub { get; private set; } = new([], [], [], 0);

	/// <summary>
	/// Loads the hub sections, bucket links, and site utility links.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A task completing when the hub data is loaded.</returns>
	public async Task OnGetAsync(CancellationToken CancellationToken)
	{
		Hub = await SitemapService.GetHtmlHubAsync(
			SiteId,
			Culture,
			Request.Scheme,
			Request.Host.Host,
			Request.Host.Port,
			CancellationToken);

		MetaDescription = $"Browse all sections, categories, files, and pages on {SiteName} ({Culture}).";
	}
}
