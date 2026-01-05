using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WikiWikiWorld.Web.Pages
{
    /// <summary>
    /// Page model for the privacy policy page.
    /// </summary>
    public sealed class PrivacyModel : PageModel
    {
        private readonly ILogger<PrivacyModel> Logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrivacyModel"/> class.
        /// </summary>
        /// <param name="Logger">The logger.</param>
        public PrivacyModel(ILogger<PrivacyModel> Logger)
        {
            this.Logger = Logger;
        }

        /// <summary>
        /// Handles the GET request for the privacy page.
        /// </summary>
        public void OnGet()
        {
        }
    }

}
