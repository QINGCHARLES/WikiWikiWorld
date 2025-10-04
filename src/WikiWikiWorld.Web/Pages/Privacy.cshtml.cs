using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WikiWikiWorld.Web.Pages
{
    public sealed class PrivacyModel : PageModel
    {
        private readonly ILogger<PrivacyModel> Logger;

        public PrivacyModel(ILogger<PrivacyModel> Logger)
        {
            this.Logger = Logger;
        }

        public void OnGet()
        {
        }
    }

}
