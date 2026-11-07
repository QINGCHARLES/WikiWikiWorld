namespace WikiWikiWorld.Web.Pages;

/// <summary>
/// Page model for the privacy policy page.
/// </summary>
public sealed class PrivacyModel : BasePageModel
{
	/// <summary>
	/// Gets the privacy contact email address configured for the current site.
	/// </summary>
	public string? PrivacyContactEmail { get; private set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="PrivacyModel"/> class.
	/// </summary>
	/// <param name="SiteResolverService">The site resolver service.</param>
	public PrivacyModel(SiteResolverService SiteResolverService)
		: base(SiteResolverService)
	{
	}

	/// <summary>
	/// Handles the GET request for the privacy page.
	/// </summary>
	public void OnGet()
	{
		PrivacyContactEmail = SiteResolverService.GetSite(SiteId)?.PrivacyContactEmail;
	}
}
