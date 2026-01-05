using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using WikiWikiWorld.Data;

namespace WikiWikiWorld.Web.Pages;

/// <summary>
/// Page model for handling 404 Not Found errors.
/// </summary>
/// <param name="SiteResolverService">The site resolver service.</param>
public class NotFoundModel(SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    /// <summary>
    /// Gets or sets the URL slug that was not found (optional).
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? UrlSlug { get; set; }

    /// <summary>
    /// Handles the GET request to display the 404 page.
    /// </summary>
    public void OnGet()
    {
        var Feature = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        if (Feature is not null)
        {
            // If the request was rewritten to Article/View, the real slug is in the query string
            if (Feature.OriginalPath.StartsWith("/Article/View", StringComparison.OrdinalIgnoreCase))
            {
                var Query = QueryHelpers.ParseQuery(Feature.OriginalQueryString);
                if (Query.TryGetValue("UrlSlug", out var Slug))
                {
                    UrlSlug = Slug;
                }
            }

            // Fallback: If UrlSlug is still null (wasn't rewritten or query param missing), use the path
            if (string.IsNullOrEmpty(UrlSlug))
            {
                string Path = Feature.OriginalPath;
                if (Path.StartsWith('/'))
                {
                    Path = Path[1..];
                }
                UrlSlug = Path;
            }
        }
    }
}
