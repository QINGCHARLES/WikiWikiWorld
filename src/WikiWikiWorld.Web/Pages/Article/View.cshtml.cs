using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Web.Services;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Page model for viewing an article or revision.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="UserManager">The user manager.</param>
/// <param name="ArticleRenderer">The article renderer.</param>
/// <param name="ArticleRevisionService">The article revision service.</param>
/// <param name="BookmarkService">The bookmark service.</param>
public sealed class ViewModel(
    WikiWikiWorldDbContext Context,
    SiteResolverService SiteResolverService,
    UserManager<User> UserManager,
    IArticleRenderer ArticleRenderer,
    ArticleRevisionService ArticleRevisionService,
    BookmarkService BookmarkService) : BasePageModel(SiteResolverService)
{
    /// <summary>
    /// Gets or sets the URL slug of the article.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string UrlSlug { get; set; } = string.Empty;

    /// <summary>
    /// Gets the raw URL slug that was requested.
    /// </summary>
    public string RequestedUrlSlug { get; private set; } = string.Empty;

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
    /// Gets the canonical article path for the displayed revision.
    /// </summary>
    public string DisplayedArticlePath => DisplayedRevision is null ? string.Empty : ArticleUrlHelper.BuildArticlePath(DisplayedRevision);

    /// <summary>
    /// Gets the canonical article path for the current revision.
    /// </summary>
    public string CurrentArticlePath => CurrentRevision is null ? string.Empty : ArticleUrlHelper.BuildArticlePath(CurrentRevision);

    /// <summary>
    /// Gets the canonical slug for the displayed revision.
    /// </summary>
    public string DisplayedCanonicalUrlSlug => DisplayedRevision is null ? string.Empty : ArticleUrlHelper.BuildCanonicalSlug(DisplayedRevision.UrlSlug, DisplayedRevision.Type);

    /// <summary>
    /// Gets the canonical slug for the current revision.
    /// </summary>
    public string CurrentCanonicalUrlSlug => CurrentRevision is null ? string.Empty : ArticleUrlHelper.BuildCanonicalSlug(CurrentRevision.UrlSlug, CurrentRevision.Type);

    /// <summary>
    /// Gets or sets the list of recent authors.
    /// </summary>
    public IReadOnlyList<ArticleAuthor> RecentAuthors { get; set; } = [];

    /// <summary>
    /// Gets or sets the categories associated with the article.
    /// </summary>
    public IReadOnlyList<Category> Categories { get; set; } = [];

    /// <summary>
    /// Gets the URL of the file image, if the article is a file with an associated image.
    /// </summary>
    public string? FileImageUrl { get; private set; }

    /// <summary>
    /// Gets the width of the file image in pixels.
    /// </summary>
    public int FileWidthPx { get; private set; }

    /// <summary>
    /// Gets the height of the file image in pixels.
    /// </summary>
    public int FileHeightPx { get; private set; }

    /// <summary>
    /// Gets the MIME type of the file.
    /// </summary>
    public string? FileMimeType { get; private set; }

    /// <summary>
    /// Gets the original filename of the uploaded file.
    /// </summary>
    public string? FileOriginalFilename { get; private set; }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    public long FileSizeBytes { get; private set; }

    /// <summary>
    /// Gets the display text of the source citation for this file, if provided.
    /// </summary>
    public string? FileSourceText { get; private set; }

    /// <summary>
    /// Gets the optional URL of the source citation for this file.
    /// </summary>
    public string? FileSourceUrl { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the FileSourceUrl is an approved domain.
    /// </summary>
    public bool IsFileSourceApprovedDomain { get; private set; }

    /// <summary>
    /// Gets the eyebrow display label extracted from the markdown, if present.
    /// </summary>
    public string? EyebrowLabel { get; private set; }

    /// <summary>
    /// Gets the subtitle text extracted from the markdown, if present.
    /// </summary>
    public string? SubtitleText { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the current user has bookmarked the displayed article.
    /// </summary>
    public bool IsBookmarked { get; private set; }

    /// <summary>
    /// Gets a value indicating whether to render the publication-upload call-to-action aside.
    /// True when the displayed revision is a standard Article that does not already embed a {{DownloadsBox ...}} extension.
    /// </summary>
    public bool ShowPublicationUploadAside =>
        DisplayedRevision is { Type: ArticleType.Article } R
        && !R.Text.Contains("{{DownloadsBox ", StringComparison.OrdinalIgnoreCase);

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

        RequestedUrlSlug = UrlSlug;
        string LookupUrlSlug = ArticleUrlHelper.NormalizeLookupSlug(UrlSlug);
        UrlSlug = LookupUrlSlug;

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

        if (DisplayedRevision is not null && ArticleUrlHelper.RequiresCanonicalRedirect(RequestedUrlSlug, DisplayedRevision.Type))
        {
            string CanonicalPath = !string.IsNullOrWhiteSpace(Revision)
                ? ArticleUrlHelper.BuildRevisionPath(DisplayedRevision, Revision)
                : ArticleUrlHelper.BuildArticlePath(DisplayedRevision);

            return RedirectPermanent(CanonicalPath);
        }

        if (DisplayedRevision is null)
        {
            // A missing @page for a real user is auto-created on first view (best-effort, any visitor)
            // and then rendered, so user pages exist as soon as anyone looks at them. A username with no
            // account, or a non-@ slug, still 404s.
            if (UrlSlug.StartsWith('@'))
            {
                string TargetUsername = UrlSlug[1..];
                User? TargetUser = await UserManager.FindByNameAsync(TargetUsername);

                if (TargetUser is null)
                {
                    return NotFound();
                }

                ArticleRevision? EnsuredArticle = await ArticleRevisionService.TryEnsureUserArticleAsync(SiteId, Culture, TargetUser, CancellationToken);

                if (EnsuredArticle is not null)
                {
                    // Render the freshly created (or concurrently created) page; fall through to the pipeline below.
                    CurrentRevision = EnsuredArticle;
                    DisplayedRevision = EnsuredArticle;
                    IsViewingCurrentRevision = true;
                }
                else
                {
                    // Creation failed (e.g. a transient error). Preserve prior behavior: send the owner to the
                    // editor to create their home page; everyone else gets a 404.
                    User? CurrentUser = await UserManager.GetUserAsync(User);
                    if (CurrentUser is not null && CurrentUser.Id == TargetUser.Id)
                    {
                        return Redirect($"/Article/CreateEdit?UrlSlug={UrlSlug}");
                    }

                    return NotFound();
                }
            }
            else
            {
                return NotFound();
            }
        }

        // Fetch recent authors (username and profile pic GUID) for this article.
        DateTimeOffset? MaxRevisionDate = IsViewingCurrentRevision ? null : DisplayedRevision.DateCreated;
        RecentAuthors = await ArticleRevisionService.GetRecentAuthorsAsync(DisplayedRevision.CanonicalArticleId, MaxRevisionDate, CancellationToken);

        // Calculate View Properties
        // Note: Logic moved from View.cshtml
        IsPriorRevision = Revision is not null && DisplayedRevision?.DateCreated != CurrentRevision?.DateCreated;
        
        if (IsPriorRevision)
        {
            HasTitleChanged = DisplayedRevision?.Title != CurrentRevision?.Title;
            HasUrlSlugChanged = DisplayedRevision?.UrlSlug != CurrentRevision?.UrlSlug;
            AllowSearchEngineIndexingOfPage = false;
        }
        else
        {
             AllowSearchEngineIndexingOfPage = true;
        }

        ArticleRevision RevisionToRender = DisplayedRevision ?? throw new InvalidOperationException("Displayed revision was not loaded.");
        string EditUrl = $"{ArticleUrlHelper.BuildArticlePath(RevisionToRender)}/edit";
        RenderedArticle RenderedArticle = await ArticleRenderer.RenderAsync(RevisionToRender, SiteId, Culture, EditUrl, CancellationToken);
        ArticleRevisionHtml = RenderedArticle.Html;
        Categories = RenderedArticle.Categories;
        HeaderImage = RenderedArticle.HeaderImage;
        MetaDescription = RenderedArticle.MetaDescription ?? string.Empty;
        EyebrowLabel = RenderedArticle.EyebrowLabel;
        SubtitleText = RenderedArticle.SubtitleText;
        FileSourceText = RenderedArticle.FileSourceText;
        FileSourceUrl = RenderedArticle.FileSourceUrl;
        IsFileSourceApprovedDomain = RenderedArticle.IsFileSourceApprovedDomain;

        // Resolve bookmark state for the displayed (canonical) article when the visitor is signed in.
        if (User.Identity?.IsAuthenticated == true)
        {
            User? CurrentUserForBookmark = await UserManager.GetUserAsync(User);
            if (CurrentUserForBookmark is not null)
            {
                IsBookmarked = await BookmarkService.IsBookmarkedAsync(RevisionToRender.CanonicalArticleId, CurrentUserForBookmark.Id, CancellationToken);
            }
        }

        // Fetch file metadata if this is a file article with an associated file
        if (RevisionToRender.Type == ArticleType.File && RevisionToRender.CanonicalFileId.HasValue)
        {
            FileRevision? CurrentFile = await Context.FileRevisions
                .AsNoTracking()
                .Where(f => f.CanonicalFileId == RevisionToRender.CanonicalFileId.Value && f.IsCurrent == true)
                .FirstOrDefaultAsync(CancellationToken);

            if (CurrentFile is not null)
            {
                FileImageUrl = ImageUrlHelper.BuildImageUrl(SiteId, CurrentFile.CanonicalFileId, CurrentFile.Id, CurrentFile.Filename);
                FileMimeType = CurrentFile.MimeType;
                FileOriginalFilename = CurrentFile.Filename;
                FileSizeBytes = CurrentFile.FileSizeBytes;

                FileWidthPx = CurrentFile.ImageWidthPixels.GetValueOrDefault();
                FileHeightPx = CurrentFile.ImageHeightPixels.GetValueOrDefault();
            }
        }

        return Page();
    }

    /// <summary>
    /// Toggles a bookmark on the displayed article for the current user.
    /// </summary>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>A redirect back to the article, a challenge if unauthenticated, or NotFound if the article cannot be resolved.</returns>
    public async Task<IActionResult> OnPostToggleBookmarkAsync(CancellationToken CancellationToken)
    {
        if (!(User.Identity?.IsAuthenticated ?? false))
        {
            return Challenge();
        }

        if (SiteId < 1 || string.IsNullOrWhiteSpace(UrlSlug))
        {
            return BadRequest("Invalid parameters.");
        }

        User? CurrentUser = await UserManager.GetUserAsync(User);
        if (CurrentUser is null)
        {
            return Challenge();
        }

        string LookupUrlSlug = ArticleUrlHelper.NormalizeLookupSlug(UrlSlug);
        ArticleRevisionsBySlugSpec CurrentSpec = new(LookupUrlSlug, IsCurrent: true);
        ArticleRevision? Current = await Context.ArticleRevisions
            .AsNoTracking()
            .WithSpecification(CurrentSpec)
            .FirstOrDefaultAsync(CancellationToken);

        if (Current is null)
        {
            return NotFound();
        }

        await BookmarkService.ToggleBookmarkAsync(Current.CanonicalArticleId, CurrentUser.Id, SiteId, CancellationToken);

        return Redirect(ArticleUrlHelper.BuildArticlePath(Current));
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

}
