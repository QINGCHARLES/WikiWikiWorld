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

	/// <summary>Gets or sets the requested page of category members; rendering clamps invalid ranges.</summary>
	[BindProperty(SupportsGet = true)]
	public int CategoryPage { get; set; } = 1;

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
    /// Gets the rendered HTML for the article's display title (including any resolved SVG logos).
    /// </summary>
    public string DisplayTitleHtml { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the single-line plain-text display title (with any logo markup stripped to fallback text).
    /// </summary>
    public string PlainTextTitle { get; private set; } = string.Empty;

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
    /// Gets the friendly codec or image format name of the displayed file (e.g., JPEG, PNG, WebP).
    /// </summary>
    public string? FileCodec { get; private set; }

    /// <summary>
    /// Gets the list of all file revision history items for this file article.
    /// </summary>
    public IReadOnlyList<FileRevisionHistoryItem> FileRevisionHistory { get; private set; } = [];

    /// <summary>
    /// Gets a value indicating whether this file article has previous (older) revisions.
    /// </summary>
    public bool HasPreviousFileRevisions => FileRevisionHistory.Count > 1;

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
    /// Gets the bookmark control state for the displayed article: the signed-in user's lists and which contain it.
    /// </summary>
    public BookmarkControlModel BookmarkControl { get; private set; } = new(string.Empty, IsSignedIn: false, []);

	/// <summary>Gets the current number of active discussion threads, including when viewing an older revision.</summary>
	public int ThreadCount { get; private set; }

	/// <summary>Gets the discussion list URL using the current article slug when available.</summary>
	public string TalkPath => (CurrentRevision ?? DisplayedRevision) is { } Article
		? ArticleUrlHelper.BuildRelativePath(Article, "talk") : string.Empty;

	/// <summary>Gets the discussion action label: an invitation ("Discuss") when there are no threads yet, otherwise the thread count.</summary>
	public string TalkLabel => ThreadCount switch
	{
		0 => "Discuss",
		1 => "1 Thread",
		_ => $"{ThreadCount} Threads"
	};

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
            IsViewingCurrentRevision = true;
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
		ThreadCount = await ArticleRevisionService.GetThreadCountAsync(DisplayedRevision.CanonicalArticleId, CancellationToken);

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
        PlainTextTitle = DisplayTitleHelper.ToPlainText(RevisionToRender.DisplayTitle, RevisionToRender.Title);
        DisplayTitleHtml = await DisplayTitleHelper.RenderHtmlAsync(
            RevisionToRender.DisplayTitle,
            RevisionToRender.Title,
            Context,
            SiteId,
            Culture,
            CancellationToken);

        string EditUrl = $"{ArticleUrlHelper.BuildArticlePath(RevisionToRender)}/edit";
        RenderedArticle RenderedArticle = await ArticleRenderer.RenderAsync(RevisionToRender, SiteId, Culture, EditUrl, CancellationToken, CategoryPage);
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
        BookmarkControl = new(DisplayedArticlePath, IsSignedIn: false, []);
        if (User.Identity?.IsAuthenticated == true)
        {
            User? CurrentUserForBookmark = await UserManager.GetUserAsync(User);
            if (CurrentUserForBookmark is not null)
            {
                IReadOnlyList<BookmarkListMembership> Lists = await BookmarkService.GetListMembershipsAsync(RevisionToRender.CanonicalArticleId, CurrentUserForBookmark.Id, CancellationToken);
                BookmarkControl = new(DisplayedArticlePath, IsSignedIn: true, Lists, BookmarkControlModel.BuildBookmarksPath(CurrentUserForBookmark.UserName));
            }
        }

        // Fetch file metadata and revision history if this is a file article with an associated file
        if (RevisionToRender.Type == ArticleType.File && RevisionToRender.CanonicalFileId.HasValue)
        {
            List<FileRevision> AllFileRevisions = await Context.FileRevisions
                .AsNoTracking()
                .Where(F => F.CanonicalFileId == RevisionToRender.CanonicalFileId.Value && F.DateDeleted == null)
                .OrderByDescending(F => F.DateCreated)
                .ThenByDescending(F => F.Id)
                .ToListAsync(CancellationToken);

            FileRevision? TargetFile = IsPriorRevision
                ? AllFileRevisions.FirstOrDefault(F => F.DateCreated <= RevisionToRender.DateCreated) ?? AllFileRevisions.LastOrDefault()
                : AllFileRevisions.FirstOrDefault(F => F.IsCurrent == true) ?? AllFileRevisions.FirstOrDefault();

            if (TargetFile is not null)
            {
                FileImageUrl = ImageUrlHelper.BuildImageUrl(SiteId, TargetFile.CanonicalFileId, TargetFile.Id, TargetFile.Filename);
                FileMimeType = TargetFile.MimeType;
                FileOriginalFilename = TargetFile.Filename;
                FileSizeBytes = TargetFile.FileSizeBytes;
                FileCodec = FormatCodecName(TargetFile.MimeType, TargetFile.Filename);

                FileWidthPx = TargetFile.ImageWidthPixels.GetValueOrDefault();
                FileHeightPx = TargetFile.ImageHeightPixels.GetValueOrDefault();
            }

            List<Guid> UploaderUserIds = [.. AllFileRevisions.Select(F => F.CreatedByUserId).Distinct()];
            Dictionary<Guid, string> UploaderNames = await Context.Users
                .AsNoTracking()
                .Where(U => UploaderUserIds.Contains(U.Id) && U.UserName != null)
                .ToDictionaryAsync(U => U.Id, U => U.UserName!, CancellationToken);

            FileRevisionHistory = [.. AllFileRevisions.Select(F => new FileRevisionHistoryItem(
                F.Id,
                F.CanonicalFileId,
                F.Filename,
                ImageUrlHelper.BuildImageUrl(SiteId, F.CanonicalFileId, F.Id, F.Filename),
                F.MimeType,
                FormatCodecName(F.MimeType, F.Filename),
                F.FileSizeBytes,
                F.ImageWidthPixels,
                F.ImageHeightPixels,
                F.IsCurrent == true,
                F.DateCreated,
                F.CreatedByUserId,
                UploaderNames.GetValueOrDefault(F.CreatedByUserId),
                F.RevisionReason))];
        }

        return Page();
    }

    /// <summary>
    /// Saves the article to, or removes it from, the default list and returns the refreshed control.
    /// </summary>
    /// <param name="Saved">
    /// The state the user asked for, rendered into the button's URL as the opposite of what the page showed. Sending the
    /// intent rather than a toggle makes repeated or concurrent clicks settle on one state.
    /// </param>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>Razor partials for Datastar requests; other callers are rejected without changing a bookmark.</returns>
    public async Task<IActionResult> OnPostSetBookmarkAsync([FromQuery] bool Saved, CancellationToken CancellationToken)
    {
        (IActionResult? Rejection, User? CurrentUser, ArticleRevision? Current) = await ResolveBookmarkRequestAsync(CancellationToken);
        if (Rejection is not null || CurrentUser is null || Current is null)
        {
            return Rejection ?? NotFound();
        }

        await BookmarkService.SetBookmarkAsync(Current.CanonicalArticleId, CurrentUser.Id, SiteId, Saved, CancellationToken);

        return await BookmarkStateResultAsync(CurrentUser, Current, CancellationToken);
    }

    /// <summary>
    /// Saves the article to, or removes it from, one of the user's bookmark lists from the split button's menu.
    /// </summary>
    /// <param name="BookmarkListId">The list to change, taken from the query string; it must belong to the current user.</param>
    /// <param name="Saved">The state the user asked for; see <see cref="OnPostSetBookmarkAsync"/>.</param>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>
    /// Razor partials that refresh the button and menu in place, leaving the open menu open; a status message when the list
    /// is not the user's. Non-Datastar callers are rejected without changing a bookmark.
    /// </returns>
    public async Task<IActionResult> OnPostSetBookmarkListAsync([FromQuery] int BookmarkListId, [FromQuery] bool Saved, CancellationToken CancellationToken)
    {
        (IActionResult? Rejection, User? CurrentUser, ArticleRevision? Current) = await ResolveBookmarkRequestAsync(CancellationToken);
        if (Rejection is not null || CurrentUser is null || Current is null)
        {
            return Rejection ?? NotFound();
        }

        bool Applied = await BookmarkService.SetBookmarkInListAsync(BookmarkListId, Current.CanonicalArticleId, CurrentUser.Id, SiteId, Saved, CancellationToken);
        if (!Applied)
        {
            return Partial("_BookmarkStatus", "That list no longer exists. Reload to see your lists.");
        }

        return await BookmarkStateResultAsync(CurrentUser, Current, CancellationToken);
    }

    /// <summary>
    /// Creates a bookmark list from the menu's "New list" field, with the current article already in it.
    /// </summary>
    /// <param name="BookmarkListName">The name typed into the field, posted with the bookmark form.</param>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>
    /// Razor partials that refresh the menu (showing the new list ticked) and reset the field; a status message, leaving
    /// the typed name in place, when the name is blank or too long or the user is at the list limit.
    /// </returns>
    public async Task<IActionResult> OnPostCreateBookmarkListAsync([FromForm] string? BookmarkListName, CancellationToken CancellationToken)
    {
        (IActionResult? Rejection, User? CurrentUser, ArticleRevision? Current) = await ResolveBookmarkRequestAsync(CancellationToken);
        if (Rejection is not null || CurrentUser is null || Current is null)
        {
            return Rejection ?? NotFound();
        }

        string Name = (BookmarkListName ?? string.Empty).Trim();
        if (Name.Length == 0)
        {
            return Partial("_BookmarkStatus", "Enter a name for the list.");
        }
        if (Name.Length > BookmarkService.MaxListNameLength)
        {
            return Partial("_BookmarkStatus", $"List names can be up to {BookmarkService.MaxListNameLength} characters.");
        }

        BookmarkListCreateResult Result = await BookmarkService.CreateListWithBookmarkAsync(Name, Current.CanonicalArticleId, CurrentUser.Id, SiteId, CancellationToken);
        if (Result is BookmarkListCreateResult.LimitReached)
        {
            return Partial("_BookmarkStatus", $"You can have up to {BookmarkService.MaxListsPerUser} lists.");
        }

        return await BookmarkStateResultAsync(CurrentUser, Current, CancellationToken);
    }

    /// <summary>
    /// Validates a bookmark POST and loads the acting user and the current revision of the requested article.
    /// </summary>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>
    /// A rejection result (bad request, sign-in prompt, or not found) with no user or article, or no rejection with both
    /// the signed-in user and the article's current revision.
    /// </returns>
    private async Task<(IActionResult? Rejection, User? CurrentUser, ArticleRevision? Current)> ResolveBookmarkRequestAsync(CancellationToken CancellationToken)
    {
        if (Request.Headers["Datastar-Request"] != "true")
        {
            return (BadRequest("Bookmark updates require an enhanced request."), null, null);
        }
        if (!(User.Identity?.IsAuthenticated ?? false))
        {
            return (Partial("_BookmarkStatus", "Sign in to bookmark articles."), null, null);
        }

        if (SiteId < 1 || string.IsNullOrWhiteSpace(UrlSlug))
        {
            return (BadRequest("Invalid parameters."), null, null);
        }

        User? CurrentUser = await UserManager.GetUserAsync(User);
        if (CurrentUser is null)
        {
            return (Partial("_BookmarkStatus", "Sign in to bookmark articles."), null, null);
        }

        string LookupUrlSlug = ArticleUrlHelper.NormalizeLookupSlug(UrlSlug);
        ArticleRevisionsBySlugSpec CurrentSpec = new(LookupUrlSlug, IsCurrent: true);
        ArticleRevision? Current = await Context.ArticleRevisions
            .AsNoTracking()
            .WithSpecification(CurrentSpec)
            .FirstOrDefaultAsync(CancellationToken);

        return Current is null ? (NotFound(), null, null) : (null, CurrentUser, Current);
    }

    /// <summary>
    /// Re-reads the user's list memberships after a toggle and renders the parts of the control that change.
    /// </summary>
    /// <param name="CurrentUser">The signed-in user.</param>
    /// <param name="Current">The article's current revision.</param>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>The state partial, whose top-level elements Datastar patches by id.</returns>
    private async Task<IActionResult> BookmarkStateResultAsync(User CurrentUser, ArticleRevision Current, CancellationToken CancellationToken)
    {
        IReadOnlyList<BookmarkListMembership> Lists = await BookmarkService.GetListMembershipsAsync(Current.CanonicalArticleId, CurrentUser.Id, CancellationToken);

        return Partial("_BookmarkState", new BookmarkControlModel(ArticleUrlHelper.BuildArticlePath(Current), IsSignedIn: true, Lists, BookmarkControlModel.BuildBookmarksPath(CurrentUser.UserName)));
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
    /// Derives a friendly image codec or format name from the MIME type and filename.
    /// </summary>
    /// <param name="MimeType">The MIME type of the file.</param>
    /// <param name="Filename">The filename of the file.</param>
    /// <returns>A friendly format name (e.g. JPEG, PNG, WebP, AVIF).</returns>
    public static string FormatCodecName(string? MimeType, string? Filename)
    {
        string CleanMime = MimeType?.Trim().ToLowerInvariant() ?? string.Empty;
        return CleanMime switch
        {
            "image/jpeg" => "JPEG",
            "image/png" => "PNG",
            "image/webp" => "WebP",
            "image/avif" => "AVIF",
            "image/gif" => "GIF",
            "image/heic" => "HEIC",
            "image/heif" => "HEIF",
            "image/svg+xml" => "SVG",
            _ => Path.GetExtension(Filename ?? string.Empty).TrimStart('.').ToUpperInvariant() switch
            {
                "JPG" or "JPEG" => "JPEG",
                "PNG" => "PNG",
                "WEBP" => "WebP",
                "AVIF" => "AVIF",
                "GIF" => "GIF",
                "HEIC" => "HEIC",
                "HEIF" => "HEIF",
                "SVG" => "SVG",
                var Ext when !string.IsNullOrWhiteSpace(Ext) => Ext,
                _ => string.IsNullOrWhiteSpace(MimeType) ? "Unknown" : MimeType
            }
        };
    }
}

/// <summary>
/// Represents a file revision entry shown in the file history list.
/// </summary>
/// <param name="Id">The unique file revision identifier.</param>
/// <param name="CanonicalFileId">The canonical file identifier.</param>
/// <param name="Filename">The original filename.</param>
/// <param name="FileImageUrl">The public image URL for this specific revision.</param>
/// <param name="MimeType">The MIME type.</param>
/// <param name="Codec">The friendly image codec or format name.</param>
/// <param name="FileSizeBytes">The file size in bytes.</param>
/// <param name="ImageWidthPixels">The image width in pixels, if known.</param>
/// <param name="ImageHeightPixels">The image height in pixels, if known.</param>
/// <param name="IsCurrent">Whether this is the current active revision.</param>
/// <param name="DateCreated">The creation date and time.</param>
/// <param name="CreatedByUserId">The identifier of the user who created this revision.</param>
/// <param name="CreatedByUserName">The username of the user who created this revision, if known.</param>
/// <param name="RevisionReason">The optional reason or upload comment.</param>
public sealed record FileRevisionHistoryItem(
    int Id,
    Guid CanonicalFileId,
    string Filename,
    string FileImageUrl,
    string MimeType,
    string Codec,
    long FileSizeBytes,
    int? ImageWidthPixels,
    int? ImageHeightPixels,
    bool IsCurrent,
    DateTimeOffset DateCreated,
    Guid CreatedByUserId,
    string? CreatedByUserName,
    string? RevisionReason);

