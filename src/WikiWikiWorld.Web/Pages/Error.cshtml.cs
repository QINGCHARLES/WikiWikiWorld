using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WikiWikiWorld.Web.Pages
{
    /// <summary>
    /// Page model for handling general application errors.
    /// </summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [IgnoreAntiforgeryToken]
    public sealed class ErrorModel : PageModel
    {
        /// <summary>
        /// Gets or sets the ID of the request that caused the error.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// Gets a value indicating whether the request ID should be displayed.
        /// </summary>
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        private readonly ILogger<ErrorModel> Logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorModel"/> class.
        /// </summary>
        /// <param name="Logger">The logger.</param>
        public ErrorModel(ILogger<ErrorModel> Logger)
        {
            this.Logger = Logger;
        }

        /// <summary>
        /// Handles the GET request to display the error page.
        /// </summary>
        public void OnGet()
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        }
    }

}
