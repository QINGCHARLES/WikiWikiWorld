namespace WikiWikiWorld.Web.Pages
{
    /// <summary>
    /// Page model for the privacy policy page.
    /// </summary>
    public sealed class PrivacyModel : BasePageModel
    {
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
        }
    }
}
