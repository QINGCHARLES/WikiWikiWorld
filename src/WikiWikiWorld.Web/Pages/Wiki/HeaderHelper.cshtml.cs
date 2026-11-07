namespace WikiWikiWorld.Web.Pages.Wiki;

/// <summary>
/// Hosts the browser-only header editor with the current wiki's branding; images are never posted here.
/// </summary>
/// <param name="SiteResolverService">Resolves the wiki and culture for the shared layout.</param>
public sealed class HeaderHelperModel(SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>Prevents the editing utility from appearing as encyclopedia content in search engines.</summary>
	public void OnGet() => AllowSearchEngineIndexingOfPage = false;
}
