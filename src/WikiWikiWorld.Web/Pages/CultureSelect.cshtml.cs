namespace WikiWikiWorld.Web.Pages
{
	/// <summary>
	/// Page model for the culture selection page.
	/// </summary>
	/// <param name="SiteResolverService">The site resolver service.</param>
	public sealed class CultureSelectModel(SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
	{
		/// <summary>
		/// Handles the GET request for the culture selection page.
		/// </summary>
		public void OnGet()
		{
		}
	}
}