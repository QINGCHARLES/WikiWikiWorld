using System.Globalization;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Page model for viewing the revision history of an article.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public sealed class ArticleHistoryModel(WikiWikiWorldDbContext Context, SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    // Bind query string parameters (SupportsGet enables binding on GET requests).
    /// <summary>
    /// Gets or sets the URL slug of the article.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string UrlSlug { get; set; } = string.Empty;

    // List of all article revisions.
    /// <summary>
    /// Gets the list of revisions for the article.
    /// </summary>
    public IReadOnlyList<ArticleRevision> ArticleRevisions { get; private set; } = [];

    /// <summary>
    /// Handles the GET request to load the revision history.
    /// </summary>
    /// <returns>The page or result.</returns>
    public async Task<IActionResult> OnGetAsync()
    {
        // Validate the query string parameter.
        if (SiteId <= 0 || string.IsNullOrWhiteSpace(Culture) || string.IsNullOrWhiteSpace(UrlSlug))
        {
            return BadRequest("Invalid parameters.");
        }

        // Retrieve all revisions for the article.
        ArticleRevisionsBySlugSpec Spec = new(UrlSlug, IsCurrent: null);
        ArticleRevisions = await Context.ArticleRevisions.WithSpecification(Spec).ToListAsync();

        if (ArticleRevisions.Count == 0)
        {
            return NotFound("No revisions found for this article.");
        }

        return Page();
    }

    /// <summary>
    /// Converts a DateTimeOffset to the 15-21 digit revision format.
    /// </summary>
    /// <param name="date">The DateTimeOffset to format.</param>
    /// <returns>A string representing the formatted revision timestamp.</returns>
    public static string FormatRevisionTimestamp(DateTimeOffset date)
    {
        return date.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture).TrimEnd('0');
    }
}
