using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using WikiWikiWorld.Data;

namespace WikiWikiWorld.Web.Pages;

public class NotFoundModel(SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    [BindProperty(SupportsGet = true)]
    public string? UrlSlug { get; set; }

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
