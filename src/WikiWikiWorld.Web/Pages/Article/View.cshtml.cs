using AngleSharp.Dom;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using WikiWikiWorld.Web.MarkdigExtensions;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Markdig.Syntax;
using System.IO;
using Microsoft.AspNetCore.Identity;
using WikiWikiWorld.Web.Services;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Page model for viewing an article or revision.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="UserManager">The user manager.</param>
/// <param name="MarkdownPipelineFactory">The markdown pipeline factory.</param>
public sealed class ViewModel(
    WikiWikiWorldDbContext Context,
    SiteResolverService SiteResolverService,
    UserManager<User> UserManager,
    IMarkdownPipelineFactory MarkdownPipelineFactory) : BasePageModel(SiteResolverService)
{
    /// <summary>
    /// Gets or sets the URL slug of the article.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string UrlSlug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the specific revision timestamp to view (optional).
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? Revision { get; set; }

    /// <summary>
    /// Gets or sets the revision currently being displayed (could be current or history).
    /// </summary>
    public ArticleRevision? DisplayedRevision { get; set; }

    /// <summary>
    /// Gets the current (latest) revision of the article.
    /// </summary>
    public ArticleRevision? CurrentRevision { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the displayed revision is the current one.
    /// </summary>
    public bool IsViewingCurrentRevision { get; private set; } = false;

    /// <summary>
    /// Gets or sets the rendered HTML content of the article.
    /// </summary>
    public string ArticleRevisionHtml { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this is a prior revision.
    /// </summary>
    public bool IsPriorRevision { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the title has changed compared to the current revision.
    /// </summary>
    public bool HasTitleChanged { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the URL slug has changed.
    /// </summary>
    public bool HasUrlSlugChanged { get; set; }

    /// <summary>
    /// Gets or sets the list of recent authors.
    /// </summary>
    // New property to hold the recent authors as a list of (username, profilePicGuid) tuples.
    public IReadOnlyList<ArticleAuthor> RecentAuthors { get; set; } = Array.Empty<ArticleAuthor>();

    /// <summary>
    /// Gets or sets the categories associated with the article.
    /// </summary>
    public IReadOnlyList<Category> Categories { get; set; } = Array.Empty<Category>();

    /// <summary>
    /// Handles the GET request to view the article.
    /// </summary>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>The page or result.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
    {
        if (SiteId < 1 || string.IsNullOrWhiteSpace(Culture) || string.IsNullOrWhiteSpace(UrlSlug))
        {
            return BadRequest("Invalid parameters.");
        }
// ... (skip unchanged lines)


        UrlSlug = UrlSlug.Replace("file:", string.Empty);

        ArticleRevision? SpecificRevision = null;

        // Check if a revision is specified
        if (!string.IsNullOrWhiteSpace(Revision) && TryParseRevisionDate(Revision, out DateTimeOffset RevisionDate))
        {
            ArticleRevisionBySlugAndDateSpec SpecificSpec = new(UrlSlug, RevisionDate);
            SpecificRevision = await Context.ArticleRevisions.WithSpecification(SpecificSpec).FirstOrDefaultAsync(CancellationToken);

            ArticleRevisionsBySlugSpec CurrentSpec = new(UrlSlug, IsCurrent: true);
            CurrentRevision = await Context.ArticleRevisions.WithSpecification(CurrentSpec).FirstOrDefaultAsync(CancellationToken);

            // Determine if this revision is the latest one
            IsViewingCurrentRevision = SpecificRevision is not null && CurrentRevision is not null && SpecificRevision.DateCreated == CurrentRevision.DateCreated;
        }
        else
        {
            ArticleRevisionsBySlugSpec CurrentSpec = new(UrlSlug, IsCurrent: true);
            CurrentRevision = await Context.ArticleRevisions.WithSpecification(CurrentSpec).FirstOrDefaultAsync(CancellationToken);
        }

        DisplayedRevision = SpecificRevision ?? CurrentRevision;

        if (DisplayedRevision is null)
        {
            // Check if this is a user home page request (starts with @)
            if (UrlSlug.StartsWith('@'))
            {
                string TargetUsername = UrlSlug[1..];
                User? TargetUser = await UserManager.FindByNameAsync(TargetUsername);

                if (TargetUser is not null)
                {
                    // Check if the current user is the owner of this profile
                    User? CurrentUser = await UserManager.GetUserAsync(User);
                    if (CurrentUser is not null && CurrentUser.Id == TargetUser.Id)
                    {
                        // Redirect to create the home page
                        return Redirect($"/Article/CreateEdit?UrlSlug={UrlSlug}");
                    }
                }
            }

            return NotFound();
        }

        // Fetch recent authors (username and profile pic GUID) for this article.
        DateTimeOffset? MaxRevisionDate = IsViewingCurrentRevision ? null : DisplayedRevision.DateCreated;
        ArticleRevisionsByCanonicalIdSpec AuthorsSpec = new(DisplayedRevision.CanonicalArticleId, MaxRevisionDate);
        
        // OPTIMIZATION: Fetch distinct User IDs directly from the database, ordered by most recent contribution.
        // We use GroupBy and OrderByDescending(Max(DateCreated)) to ensure the list is sorted by recency.
        List<Guid> DistinctUserIds = await Context.ArticleRevisions
            .WithSpecification(AuthorsSpec)
            .GroupBy(r => r.CreatedByUserId)
            .Select(g => new { UserId = g.Key, MaxDate = g.Max(r => r.DateCreated) })
            .OrderByDescending(x => x.MaxDate)
            .Select(x => x.UserId)
            .ToListAsync(CancellationToken);

        // Fetch all users in one query
        UserByIdsSpec UserSpec = new(DistinctUserIds);
        List<User> Users = await Context.Users.WithSpecification(UserSpec).ToListAsync(CancellationToken);
        
        List<ArticleAuthor> Authors = [];
        // Iterate through DistinctUserIds to preserve the order returned by the query
        foreach (Guid UserId in DistinctUserIds)
        {
            User? User = Users.FirstOrDefault(u => u.Id == UserId);
            if (User is not null && User.UserName is not null)
            {
                Authors.Add(new ArticleAuthor(User.UserName, User.ProfilePicGuid));
            }
        }
        RecentAuthors = Authors;

        // Calculate View Properties
        // Note: Logic moved from View.cshtml
        IsPriorRevision = Revision is not null && DisplayedRevision?.DateCreated != CurrentRevision?.DateCreated;
        
        if (IsPriorRevision)
        {
            HasTitleChanged = DisplayedRevision?.Title != CurrentRevision?.Title;
            HasUrlSlugChanged = UrlSlug != CurrentRevision?.UrlSlug;
            AllowSearchEngineIndexingOfPage = false;
        }
        else
        {
             AllowSearchEngineIndexingOfPage = true;
        }

        // Markdown processing
        MarkdownPipeline Pipeline = MarkdownPipelineFactory.GetPipeline();
        MarkdownDocument Document = Markdown.Parse(DisplayedRevision!.Text, Pipeline);

        // Enrich the document with async data
        ShortDescriptionExtension.Enrich(Document);
        await ImageExtension.EnrichAsync(Document, Context, SiteId, Culture);
        await HeaderImageExtension.EnrichAsync(Document, Context, SiteId, Culture, CancellationToken);
        await PublicationIssueInfoboxExtension.EnrichAsync(Document, Context, SiteId, Culture, CancellationToken);
        await CoverGridExtension.EnrichAsync(Document, Context, SiteId, Culture, CancellationToken);
        await DownloadsBoxExtension.EnrichAsync(Document, Context, SiteId, CancellationToken);

        // Extract metadata and reprocess content
        Categories = CategoryExtension.GetCategories(Document);

        FootnoteExtension.ReprocessFootnotes(Document, Pipeline);
        CitationExtension.ReprocessCitations(Document);

        if (Document.GetData(HeaderImageExtension.DocumentKey) is string ResolvedHeaderImage)
        {
            HeaderImage = ResolvedHeaderImage;
        }

        if (Document.GetData(ShortDescriptionExtension.DocumentKey) is string ResolvedDescription)
        {
            MetaDescription = ResolvedDescription;
        }

        // Render to HTML
        StringWriter Writer = new();
        HtmlRenderer Renderer = new(Writer);
        Pipeline.Setup(Renderer);
        Renderer.Render(Document);
        Writer.Flush();
        
        ArticleRevisionHtml = CleanupAndStyleArticle(Writer.ToString());

        return Page();
    }

    /// <summary>
    /// Tries to parse a revision date string into a DateTimeOffset.
    /// </summary>
    /// <param name="Revision">The revision string (timestamp).</param>
    /// <param name="DateTime">The parsed DateTimeOffset.</param>
    /// <returns>True if parsing was successful; otherwise, false.</returns>
    private static bool TryParseRevisionDate(string Revision, out DateTimeOffset DateTime)
    {
        DateTime = default;

        if (Revision.Length == 14)
        {
            if (DateTimeOffset.TryParseExact(
                Revision, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out DateTimeOffset ParsedDate14))
            {
                DateTime = ParsedDate14;
                return true;
            }
        }
        else if (Revision.Length >= 15 && Revision.Length <= 21)
        {
            string NormalizedRevision = Revision.PadRight(21, '0'); // Ensure proper format

            if (DateTimeOffset.TryParseExact(
                NormalizedRevision, "yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out DateTimeOffset ParsedDateFull))
            {
                DateTime = ParsedDateFull;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cleans up and styles the article HTML content.
    /// Removes empty paragraph tags and styles the final period with a distinctive end mark.
    /// </summary>
    /// <param name="Html">The rendered HTML content.</param>
    /// <returns>The cleaned and styled HTML.</returns>
    private static string CleanupAndStyleArticle(string Html)
    {
        // First, strip empty paragraph tags (handles both <p></p> and <p> </p> with whitespace)
        Html = System.Text.RegularExpressions.Regex.Replace(
            Html, 
            @"<p>\s*</p>", 
            string.Empty, 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Find the last occurrence of a period followed by closing tags or end of content
        // We look for a period that ends actual content (before </p> or similar)
        int LastParagraphClose = Html.LastIndexOf("</p>", StringComparison.OrdinalIgnoreCase);
        if (LastParagraphClose < 0)
        {
            return Html;
        }

        // Search backwards from the </p> to find the last period
        int SearchStart = LastParagraphClose - 1;
        while (SearchStart >= 0)
        {
            char c = Html[SearchStart];
            if (c == '.')
            {
                // Found a period - wrap it in a span
                return string.Concat(
                    Html.AsSpan(0, SearchStart),
                    "<span class=\"article-end-mark\">.</span>",
                    Html.AsSpan(SearchStart + 1));
            }
            else if (char.IsLetterOrDigit(c) || c == '"' || c == '\'' || c == ')' || c == ']')
            {
                // Hit actual content without finding a period, stop searching
                break;
            }
            SearchStart--;
        }

        return Html;
    }
}
