using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WikiWikiWorld.Web.Pages
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [IgnoreAntiforgeryToken]
    public sealed class ErrorModel : PageModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        private readonly ILogger<ErrorModel> Logger;

        public ErrorModel(ILogger<ErrorModel> Logger)
        {
            this.Logger = Logger;
        }

        public void OnGet()
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        }
    }

}
