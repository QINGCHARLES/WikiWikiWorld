using AngleSharp.Dom;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using WikiWikiWorld.MarkdigExtensions;
using WikiWikiWorld.Web.MarkdigExtensions;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Markdig.Syntax;
using System.IO;

namespace WikiWikiWorld.Web.Pages.Article;

public sealed class ViewModel(
    WikiWikiWorldDbContext Context,
    SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    [BindProperty(SupportsGet = true)]
    public string UrlSlug { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? Revision { get; set; }

    public ArticleRevision? DisplayedRevision { get; set; }
    public ArticleRevision? CurrentRevision { get; private set; }
    public bool IsViewingCurrentRevision { get; private set; } = false;
    public string ArticleRevisionHtml { get; set; } = string.Empty;

    // New property to hold the recent authors as a list of (username, profilePicGuid) tuples.
    public IReadOnlyList<ArticleAuthor> RecentAuthors { get; set; } = Array.Empty<ArticleAuthor>();

    public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
    {
        if (SiteId < 1 || string.IsNullOrWhiteSpace(Culture) || string.IsNullOrWhiteSpace(UrlSlug))
        {
            return BadRequest("Invalid parameters.");
        }

        UrlSlug = UrlSlug.Replace("file:", string.Empty);

        ArticleRevision? SpecificRevision = null;

        // Check if a revision is specified
        if (!string.IsNullOrWhiteSpace(Revision) && TryParseRevisionDate(Revision, out DateTimeOffset RevisionDate))
        {
            ArticleRevisionBySlugAndDateSpec SpecificSpec = new(SiteId, Culture, UrlSlug, RevisionDate);
            SpecificRevision = await Context.ArticleRevisions.WithSpecification(SpecificSpec).FirstOrDefaultAsync(CancellationToken);

            ArticleRevisionsBySlugSpec CurrentSpec = new(SiteId, Culture, UrlSlug, IsCurrent: true);
            CurrentRevision = await Context.ArticleRevisions.WithSpecification(CurrentSpec).FirstOrDefaultAsync(CancellationToken);

            // Determine if this revision is the latest one
            IsViewingCurrentRevision = SpecificRevision is not null && CurrentRevision is not null && SpecificRevision.DateCreated == CurrentRevision.DateCreated;
        }
        else
        {
            ArticleRevisionsBySlugSpec CurrentSpec = new(SiteId, Culture, UrlSlug, IsCurrent: true);
            CurrentRevision = await Context.ArticleRevisions.WithSpecification(CurrentSpec).FirstOrDefaultAsync(CancellationToken);
        }

        DisplayedRevision = SpecificRevision ?? CurrentRevision;

        if (DisplayedRevision is null)
        {
            return NotFound();
        }

        // Fetch recent authors (username and profile pic GUID) for this article.
        DateTimeOffset? MaxRevisionDate = IsViewingCurrentRevision ? null : DisplayedRevision.DateCreated;
        ArticleRevisionsByCanonicalIdSpec AuthorsSpec = new(DisplayedRevision.CanonicalArticleId, MaxRevisionDate);
        IReadOnlyList<ArticleRevision> Revisions = await Context.ArticleRevisions.WithSpecification(AuthorsSpec).ToListAsync(CancellationToken);

        // Get distinct User IDs preserving order of appearance (Recent first)
        List<Guid> DistinctUserIds = Revisions.Select(r => r.CreatedByUserId).Distinct().ToList();
        
        // Fetch all users in one query
        UserByIdsSpec UserSpec = new(DistinctUserIds);
        List<User> Users = await Context.Users.WithSpecification(UserSpec).ToListAsync(CancellationToken);
        
        List<ArticleAuthor> Authors = [];
        foreach (Guid UserId in DistinctUserIds)
        {
            User? User = Users.FirstOrDefault(u => u.Id == UserId);
            if (User is not null && User.UserName is not null)
            {
                Authors.Add(new ArticleAuthor(User.UserName, User.ProfilePicGuid));
            }
        }
        RecentAuthors = Authors;

        // Markdown processing
        ShortDescriptionExtension ShortDescExt = new(this);
        ImageExtension ImageExt = new(SiteId);
        HeaderImageExtension HeaderImageExt = new(SiteId, this);
        DownloadsBoxExtension DownloadsBoxExt = new();
        PullQuoteExtension PullQuoteExt = new();
        TestExtension TestExt = new();

        List<Category> Categories = [];
        CategoriesExtension CategoriesExt = new(Categories);
        CategoryExtension CategoryExt = new(Categories);

        List<Footnote> Footnotes = [];
        FootnotesExtension FootnotesExt = new(Footnotes);
        FootnoteExtension FootnoteExt = new(Footnotes);

        Dictionary<string, Citation> Citations = [];
        CitationsExtension CitationsExt = new(Citations);
        CitationExtension CitationExt = new(Citations);

        PublicationIssueInfoboxExtension PublicationIssueInfoboxExt = new();
        CoverGridExtension CoverGridExt = new(Culture);

        MarkdownPipelineBuilder Builder = new MarkdownPipelineBuilder()
                            .Use(ShortDescExt)
                            .Use(TestExt)
                            .Use(ImageExt)
                            .Use(HeaderImageExt)
                            .Use(CategoriesExt)
                            .Use(CategoryExt)
                            .Use(FootnotesExt)
                            .Use(FootnoteExt)
                            .Use(CitationsExt)
                            .Use(CitationExt)
                            .Use(PublicationIssueInfoboxExt)
                            .Use(CoverGridExt)
                            .Use(DownloadsBoxExt)
                            .Use(PullQuoteExt)
                            .UseAdvancedExtensions();

        MarkdownPipeline Pipeline = Builder.Build();
        MarkdownDocument Document = Markdown.Parse(DisplayedRevision.Text, Pipeline);

        // Enrich the document with async data
        await ImageExtension.EnrichAsync(Document, Context, SiteId, Culture);
        await HeaderImageExtension.EnrichAsync(Document, Context, SiteId, Culture, CancellationToken);
        await PublicationIssueInfoboxExtension.EnrichAsync(Document, Context, SiteId, Culture, CancellationToken);
        await CoverGridExtension.EnrichAsync(Document, Context, SiteId, Culture, CancellationToken);
        await DownloadsBoxExtension.EnrichAsync(Document, Context, SiteId, CancellationToken);

        // Render to HTML
        StringWriter Writer = new();
        HtmlRenderer Renderer = new(Writer);
        Pipeline.Setup(Renderer);
        Renderer.Render(Document);
        Writer.Flush();
        
        ArticleRevisionHtml = Writer.ToString();

        return Page();
    }

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
}
