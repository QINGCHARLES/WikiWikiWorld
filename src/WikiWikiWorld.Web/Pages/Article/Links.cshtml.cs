using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Web.Services;
using WikiWikiWorld.Web.Helpers;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Page model for viewing backlinks to an article.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public class LinksModel(
    WikiWikiWorldDbContext Context,
    SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    /// <summary>
    /// Gets or sets the requested article slug.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string UrlSlug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current article revision.
    /// </summary>
    public ArticleRevision? CurrentArticle { get; set; }

    /// <summary>
    /// Gets or sets the list of backlinking articles.
    /// </summary>
    public List<ArticleRevision> Backlinks { get; set; } = [];

    /// <summary>
    /// Gets the canonical article path for the current article.
    /// </summary>
    public string CanonicalArticlePath => CurrentArticle is null ? string.Empty : ArticleUrlHelper.BuildArticlePath(CurrentArticle);

    /// <summary>
    /// Handles the GET request to view backlinks for an article.
    /// </summary>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>The page or a redirect when the request is not canonical.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
    {
        if (SiteId < 1 || string.IsNullOrWhiteSpace(Culture) || string.IsNullOrWhiteSpace(UrlSlug))
        {
            return BadRequest("Invalid parameters.");
        }

        string RequestedUrlSlug = UrlSlug;
        string LookupUrlSlug = ArticleUrlHelper.NormalizeLookupSlug(UrlSlug);
        UrlSlug = LookupUrlSlug;

        // 1. Get the current article to ensure it exists and to display its title
        ArticleRevisionsBySlugSpec CurrentSpec = new(LookupUrlSlug, IsCurrent: true);
        CurrentArticle = await Context.ArticleRevisions.AsQueryable().WithSpecification(CurrentSpec).FirstOrDefaultAsync(CancellationToken);

        if (CurrentArticle is null)
        {
            return NotFound();
        }

        if (ArticleUrlHelper.RequiresCanonicalRedirect(RequestedUrlSlug, CurrentArticle.Type))
        {
            return RedirectPermanent(ArticleUrlHelper.BuildRelativePath(CurrentArticle, "links"));
        }

        // 2. Find backlinks
        // We look for revisions that are current, not deleted, and contain the link syntax for this slug.
        // Link syntax: [Link Text](UrlSlug) or [Link Text](/UrlSlug)
        // We will use a broader LIKE search and then filter if needed, but for now LIKE '%(UrlSlug)%' is a decent start
        // To be more precise: LIKE '%](urlslug)%' or LIKE '%](urlslug%' (to catch anchors?)
        // Let's settle on checking for the slug being inside parenthesis which follows a bracket.
        // EF Core Translate: string.Contains translates to LIKE '%search%'
        
        string SearchTerm1 = $"]({UrlSlug})";
        string SearchTerm2 = $"](/{UrlSlug})";
        string FileSearchTerm1 = $"](file:{UrlSlug})";
        string FileSearchTerm2 = $"](/file:{UrlSlug})";
        bool IncludeFileNamespaceTerms = CurrentArticle.Type == ArticleType.File;

        Backlinks = await Context.ArticleRevisions
            .Where(r => r.SiteId == SiteId 
                        && r.Culture == Culture 
                        && r.IsCurrent 
                        && r.DateDeleted == null
                        && (
                            r.Text.Contains(SearchTerm1) ||
                            r.Text.Contains(SearchTerm2) ||
                            (IncludeFileNamespaceTerms &&
                                (r.Text.Contains(FileSearchTerm1) || r.Text.Contains(FileSearchTerm2)))))
            .OrderByDescending(r => r.DateCreated)
            .ToListAsync(CancellationToken);

        return Page();
    }
}
