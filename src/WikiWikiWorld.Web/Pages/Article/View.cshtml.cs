using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Web.MarkdigExtensions;
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
/// <param name="PublicationHeadBuilder">Builds catalogue metadata for current publication articles.</param>
public sealed class ViewModel(
    WikiWikiWorldDbContext Context,
    SiteResolverService SiteResolverService,
    UserManager<User> UserManager,
    IArticleRenderer ArticleRenderer,
    ArticleRevisionService ArticleRevisionService,
    BookmarkService BookmarkService,
    PublicationHeadBuilder PublicationHeadBuilder) : BasePageModel(SiteResolverService)
{
	/// <summary>Gets the hero's image description, or an empty value for decorative artwork.</summary>
	public string HeaderImageAltText { get; private set; } = string.Empty;

	/// <summary>Gets the metadata for a current publication or issue article.</summary>
	public PublicationHead? Head { get; private set; }

	/// <summary>Gets the reciprocal language links for a current Magazedia article.</summary>
	public IReadOnlyList<PublicationAlternate> LanguageAlternates { get; private set; } = [];

	/// <summary>Holds the parsed publication facts for an explicit citation download request.</summary>
	private PublicationFacts? CitationFacts;

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
    /// Gets the header's fit-to-width custom properties from <see cref="DisplayTitleHelper.BuildFitStyle"/>,
    /// or <see langword="null"/> when the title has no text words.
    /// </summary>
    public string? DisplayTitleFitStyle { get; private set; }

    /// <summary>
    /// Gets the displayed revision's canonical title for the browser title and image alternative text,
    /// preserving punctuation independently of the optional formatted display heading.
    /// </summary>
    public string PlainTextTitle { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether a specific revision was loaded and is no longer the current one.
    /// </summary>
    public bool IsPriorRevision { get; private set; }

	/// <summary>Gets the read-only source URL when viewing a previous revision.</summary>
	public string? RevisionSourcePath => IsPriorRevision && DisplayedRevision is { } Previous && Revision is { } Timestamp
		? $"{ArticleUrlHelper.BuildRevisionPath(Previous, Timestamp)}/source"
		: null;

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
    /// TempData key storing the source title of a completed redirect.
    /// </summary>
    public const string RedirectedFromTitleTempDataKey = "RedirectedFromTitle";

    /// <summary>
    /// TempData key storing the resolved destination slug so the banner renders only on the target page.
    /// </summary>
    public const string RedirectedToSlugTempDataKey = "RedirectedToSlug";

    /// <summary>
    /// Gets the title of the redirect page that forwarded the visitor here, if any.
    /// </summary>
    public string? RedirectedFromTitle { get; private set; }

    /// <summary>
    /// TempData key storing the save warnings (infobox problems, email addresses, raw YouTube addresses) found when the editor last saved, shown once on the saved article.
    /// </summary>
    public const string SaveWarningsTempDataKey = "SaveWarnings";

    /// <summary>
    /// TempData key storing the slug the save warnings belong to, so they show only on that article.
    /// </summary>
    public const string SaveWarningsSlugTempDataKey = "SaveWarningsSlug";

    /// <summary>
    /// Gets the save warnings from the save that led here; empty unless this viewer just saved this article.
    /// </summary>
    public IReadOnlyList<string> SaveWarnings { get; private set; } = [];

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
    /// Gets the eyebrow type extracted from the markdown, or <see langword="null"/> when the article has no valid eyebrow.
    /// </summary>
    public EyebrowType? Eyebrow { get; private set; }

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
	public string TalkLabel => ThreadCount == 0
		? SharedResource.Text("Discuss")
		: SharedResource.Plural("Talk.Threads", ThreadCount);

    /// <summary>
    /// Gets a value indicating whether to render the publication-upload call-to-action aside.
    /// True when the displayed revision is a standard Article whose eyebrow marks a single publication issue
    /// (magazine, zine, journal or newsletter) and that does not already embed a {{DownloadsBox ...}} extension.
    /// </summary>
    public bool ShowPublicationUploadAside =>
        DisplayedRevision is { Type: ArticleType.Article } R
        && Eyebrow is { } Type && EyebrowExtension.IsPublicationIssue(Type)
        && !R.Text.Contains("{{DownloadsBox ", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Loads the requested revision and resolves its current article by identity even after a rename.
    /// Uses the saved title for metadata and the optional display title for the formatted heading.
    /// Builds the head for a current Magazedia publication or issue.
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

		if (!string.IsNullOrWhiteSpace(Revision) && RevisionDateParser.TryParseRevisionRange(Revision, out DateTimeOffset RevisionFrom, out DateTimeOffset RevisionUntil))
		{
			ArticleRevisionBySlugAndDateSpec SpecificSpec = new(UrlSlug, RevisionFrom, RevisionUntil);
			SpecificRevision = await Context.ArticleRevisions.WithSpecification(SpecificSpec).FirstOrDefaultAsync(CancellationToken);
		}
		if (SpecificRevision is null)
		{
			ArticleRevisionsBySlugSpec CurrentSpec = new(UrlSlug, IsCurrent: true);
			CurrentRevision = await Context.ArticleRevisions.WithSpecification(CurrentSpec).FirstOrDefaultAsync(CancellationToken);
		}
		else
		{
			CurrentRevision = SpecificRevision.IsCurrent ? SpecificRevision : await Context.ArticleRevisions
				.AsNoTracking()
				.FirstOrDefaultAsync(r => r.CanonicalArticleId == SpecificRevision.CanonicalArticleId && r.IsCurrent, CancellationToken);
		}
		IsPriorRevision = SpecificRevision is { IsCurrent: false };
		IsViewingCurrentRevision = !IsPriorRevision;

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
                        return Redirect($"/Article/CreateEdit?urlslug={Uri.EscapeDataString(UrlSlug)}");
                    }

                    return NotFound();
                }
            }
            else
            {
                // Temporary, not permanent: browsers cache a 301 indefinitely, and a later article may reuse the old slug.
                if (string.IsNullOrWhiteSpace(Revision) && await FindRenamedArticleAsync(UrlSlug, CancellationToken) is { } RenamedArticle)
                {
                    return Redirect(ArticleUrlHelper.BuildArticlePath(RenamedArticle));
                }

                return NotFound();
            }
        }

        if (string.IsNullOrWhiteSpace(Revision)
            && DisplayedRevision.Type == ArticleType.Redirect
            && RedirectExtension.TryExtractTarget(DisplayedRevision.Text, out string RedirectTargetSlug, out string? RedirectFragment)
            && !RedirectTargetSlug.Equals(DisplayedRevision.UrlSlug, StringComparison.OrdinalIgnoreCase))
        {
            ArticleRevisionsBySlugSpec TargetSpec = new(RedirectTargetSlug, IsCurrent: true);
            ArticleRevision? TargetRevision = await Context.ArticleRevisions
                .AsNoTracking()
                .WithSpecification(TargetSpec)
                .FirstOrDefaultAsync(CancellationToken);

            if (TargetRevision is not null && TargetRevision.Type != ArticleType.Redirect)
            {
                if (TempData is not null)
                {
                    TempData[RedirectedFromTitleTempDataKey] = DisplayedRevision.Title;
                    TempData[RedirectedToSlugTempDataKey] = TargetRevision.UrlSlug;
                }

                string TargetPath = ArticleUrlHelper.BuildArticlePath(TargetRevision);
                if (!string.IsNullOrEmpty(RedirectFragment))
                {
                    TargetPath = $"{TargetPath}#{RedirectFragment}";
                }

                return Redirect(TargetPath);
            }
        }

        ConsumeRedirectNotice(DisplayedRevision);
        ConsumeSaveWarnings(DisplayedRevision);

        // Fetch recent authors (username and profile pic GUID) for this article.
        DateTimeOffset? MaxRevisionDate = IsViewingCurrentRevision ? null : DisplayedRevision.DateCreated;
        RecentAuthors = await ArticleRevisionService.GetRecentAuthorsAsync(DisplayedRevision.CanonicalArticleId, MaxRevisionDate, CancellationToken);
		ThreadCount = await ArticleRevisionService.GetThreadCountAsync(DisplayedRevision.CanonicalArticleId, CancellationToken);

        if (IsPriorRevision)
        {
            HasTitleChanged = DisplayedRevision?.Title != CurrentRevision?.Title;
            HasUrlSlugChanged = DisplayedRevision?.UrlSlug != CurrentRevision?.UrlSlug;
            AllowSearchEngineIndexingOfPage = false;
        }
        else
        {
            AllowSearchEngineIndexingOfPage = DisplayedRevision?.Type != ArticleType.Redirect;
        }

        ArticleRevision RevisionToRender = DisplayedRevision ?? throw new InvalidOperationException("Displayed revision was not loaded.");
        PlainTextTitle = RevisionToRender.Title;
        DisplayTitleHtml = await DisplayTitleHelper.RenderHtmlAsync(
            RevisionToRender.DisplayTitle,
            RevisionToRender.Title,
            Context,
            SiteId,
            Culture,
            CancellationToken);
        DisplayTitleFitStyle = DisplayTitleHelper.BuildFitStyle(RevisionToRender.DisplayTitle, RevisionToRender.Title);

        string EditUrl = IsPriorRevision ? string.Empty : ArticleUrlHelper.BuildRelativePath(RevisionToRender, "edit");
        RenderedArticle RenderedArticle = await ArticleRenderer.RenderAsync(RevisionToRender, SiteId, Culture, EditUrl, CancellationToken, CategoryPage);
        ArticleRevisionHtml = RenderedArticle.Html;
        Categories = RenderedArticle.Categories;
        HeaderImage = RenderedArticle.HeaderImage;
        HeaderImageFocus = RenderedArticle.HeaderImageFocus;
        HeaderImageAltText = RenderedArticle.HeaderImageAltText ?? string.Empty;
        MetaDescription = RenderedArticle.MetaDescription ?? string.Empty;
		if (AllowSearchEngineIndexingOfPage && SiteId == 1 && RevisionToRender.Type == ArticleType.Article
			&& SiteResolverService.GetSite(SiteId) is { } Site)
		{
			if (RenderedArticle.Publication is { } Publication)
			{
				Head = await PublicationHeadBuilder.BuildAsync(RevisionToRender, RenderedArticle, Publication, Site, Request, CancellationToken);
				MetaDescription = Head.Description ?? string.Empty;
				CitationFacts = Publication;
			}
			LanguageAlternates = await PublicationHeadBuilder.GetAlternatesAsync(RevisionToRender, Site, Request, CancellationToken);
		}
        EyebrowLabel = RenderedArticle.EyebrowLabel;
        Eyebrow = RenderedArticle.Eyebrow;
        SubtitleText = RenderedArticle.SubtitleText;
        FileSourceText = RenderedArticle.FileSourceText;
        FileSourceUrl = RenderedArticle.FileSourceUrl;
        IsFileSourceApprovedDomain = RenderedArticle.IsFileSourceApprovedDomain;

        BookmarkControl = new(CurrentArticlePath, IsSignedIn: false, []);
        if (CurrentRevision is { } CurrentArticle && User.Identity?.IsAuthenticated == true)
        {
            User? CurrentUserForBookmark = await UserManager.GetUserAsync(User);
            if (CurrentUserForBookmark is not null)
            {
                IReadOnlyList<BookmarkListMembership> Lists = await BookmarkService.GetListMembershipsAsync(CurrentArticle.CanonicalArticleId, CurrentUserForBookmark.Id, CancellationToken);
                BookmarkControl = new(CurrentArticlePath, IsSignedIn: true, Lists, BookmarkControlModel.BuildBookmarksPath(CurrentUserForBookmark.UserName),
                    ArticleTitle: CurrentArticle.Title);
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

	/// <summary>Downloads a RIS citation for the current issue or its Magazedia entry.</summary>
	/// <param name="Kind">The item to cite, either issue or page.</param>
	/// <param name="CancellationToken">Cancels resolution and rendering of the article.</param>
	/// <returns>A RIS attachment, a canonical redirect, or a missing-item response.</returns>
	public async Task<IActionResult> OnGetCitationAsync(string Kind, CancellationToken CancellationToken)
	{
		if (SiteId != 1 || Kind is not ("issue" or "page") || UrlSlug.StartsWith('@'))
			return NotFound();

		IActionResult Result = await OnGetAsync(CancellationToken);
		if (Result is not Microsoft.AspNetCore.Mvc.RazorPages.PageResult) return Result;
		if (Head is not { } Metadata || CitationFacts is not { } Facts || DisplayedRevision is not { } Article
			|| (Kind == "issue" && !Facts.IsIssue))
		{
			return NotFound();
		}

		PublicationCitation Citation;
		if (Kind == "issue")
		{
			Citation = PublicationCitation.ForIssue(Metadata, Facts);
		}
		else
		{
			string RevisionPath = ArticleUrlHelper.BuildRevisionPath(Article,
				ArticleHistoryModel.FormatRevisionTimestamp(Article.DateCreated));
			string Permalink = new Uri(new Uri(Metadata.CanonicalUrl), RevisionPath).AbsoluteUri;
			Citation = PublicationCitation.ForPage(Metadata, Article, Permalink);
		}

		Response.Headers["X-Robots-Tag"] = "noindex";
		return File(Encoding.UTF8.GetBytes(Citation.Ris), "application/x-research-info-systems", $"magazedia-{Kind}.ris");
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

        return await BookmarkStateResultAsync(CurrentUser, Current, CancellationToken, SavedList: Saved ? l => l.IsDefault : null);
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
            return Partial("_BookmarkStatus", SharedResource.Text("That list no longer exists. Reload to see your lists."));
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
            return Partial("_BookmarkStatus", SharedResource.Text("Enter a name for the list."));
        }
        if (Name.Length > BookmarkService.MaxListNameLength)
        {
            return Partial("_BookmarkStatus", SharedResource.Format("List names can be up to {0} characters.", BookmarkService.MaxListNameLength));
        }

        BookmarkListCreateResult Result = await BookmarkService.CreateListWithBookmarkAsync(Name, Current.CanonicalArticleId, CurrentUser.Id, SiteId, CancellationToken);
        if (Result is BookmarkListCreateResult.LimitReached)
        {
            return Partial("_BookmarkStatus", SharedResource.Format("You can have up to {0} lists.", BookmarkService.MaxListsPerUser));
        }

        return await BookmarkStateResultAsync(CurrentUser, Current, CancellationToken);
    }

    /// <summary>
    /// Saves or clears the note on one of the user's memberships of this article, from the note dialog.
    /// </summary>
    /// <param name="BookmarkId">The membership to annotate, posted by the dialog; it must belong to the current user.</param>
    /// <param name="Notes">Markdown source; blank clears the note.</param>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>
    /// Razor partials that refresh the menu with the new note and confirm it in the status line; a status message when the
    /// note is too long or the bookmark was removed meanwhile. Non-Datastar callers are rejected without changing a note.
    /// </returns>
    public async Task<IActionResult> OnPostSetBookmarkNoteAsync([FromForm] int BookmarkId, [FromForm] string? Notes, CancellationToken CancellationToken)
    {
        (IActionResult? Rejection, User? CurrentUser, ArticleRevision? Current) = await ResolveBookmarkRequestAsync(CancellationToken);
        if (Rejection is not null || CurrentUser is null || Current is null)
        {
            return Rejection ?? NotFound();
        }
        if ((Notes?.Length ?? 0) > Bookmark.MaxNotesLength)
        {
            return Partial("_BookmarkStatus", SharedResource.Format("Notes can be up to {0:N0} characters.", Bookmark.MaxNotesLength));
        }

        IReadOnlyList<BookmarkListMembership> Before = await BookmarkService.GetListMembershipsAsync(Current.CanonicalArticleId, CurrentUser.Id, CancellationToken);
        BookmarkListMembership? Target = Before.FirstOrDefault(l => l.BookmarkId == BookmarkId);
        bool Saved = Target is not null && await BookmarkService.SetBookmarkNotesAsync(BookmarkId, Notes, CurrentUser.Id, CancellationToken);
        string Message = !Saved
            ? SharedResource.Text("That bookmark was removed, so the note was not saved. Reload to see your lists.")
            : string.IsNullOrWhiteSpace(Notes) ? SharedResource.Format("Note removed from {0}.", Target!.Name) : SharedResource.Format("Note saved to {0}.", Target!.Name);

        return await BookmarkStateResultAsync(CurrentUser, Current, CancellationToken, Message);
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
            return (Partial("_BookmarkStatus", SharedResource.Text("Sign in to bookmark articles.")), null, null);
        }

        if (SiteId < 1 || string.IsNullOrWhiteSpace(UrlSlug))
        {
            return (BadRequest("Invalid parameters."), null, null);
        }

        User? CurrentUser = await UserManager.GetUserAsync(User);
        if (CurrentUser is null)
        {
            return (Partial("_BookmarkStatus", SharedResource.Text("Sign in to bookmark articles.")), null, null);
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
    /// <param name="StatusMessage">A confirmation for the status line; empty clears it.</param>
    /// <param name="SavedList">Picks the list the article was just added to, for the "Add a note" prompt; null when nothing was added.</param>
    /// <returns>The state partial, whose top-level elements Datastar patches by id.</returns>
    private async Task<IActionResult> BookmarkStateResultAsync(User CurrentUser, ArticleRevision Current, CancellationToken CancellationToken,
        string StatusMessage = "", Func<BookmarkListMembership, bool>? SavedList = null)
    {
        IReadOnlyList<BookmarkListMembership> Lists = await BookmarkService.GetListMembershipsAsync(Current.CanonicalArticleId, CurrentUser.Id, CancellationToken);
        int? SavedListId = SavedList is null ? null : Lists.FirstOrDefault(SavedList)?.BookmarkListId;

        return Partial("_BookmarkState", new BookmarkControlModel(ArticleUrlHelper.BuildArticlePath(Current), IsSignedIn: true, Lists,
            BookmarkControlModel.BuildBookmarksPath(CurrentUser.UserName), StatusMessage, SavedListId));
    }

    /// <summary>
    /// Finds the article a slug used to belong to before it was renamed, so the old address can redirect.
    /// Takes the newest non-current revision stored under the slug and returns its article's current revision.
    /// Soft-deleted articles are excluded by the query filter, and the caller only asks after no current
    /// article holds the slug, so a page that later takes over the slug always wins.
    /// </summary>
    /// <param name="UrlSlug">The normalized slug that has no current article.</param>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>The renamed article's current revision, or <see langword="null"/> when the slug was never renamed away.</returns>
    private async Task<ArticleRevision?> FindRenamedArticleAsync(string UrlSlug, CancellationToken CancellationToken)
    {
        ArticleRevisionsBySlugSpec FormerSpec = new(UrlSlug, IsCurrent: false);
        ArticleRevision? Former = await Context.ArticleRevisions
            .AsNoTracking()
            .WithSpecification(FormerSpec)
            .FirstOrDefaultAsync(CancellationToken);

        if (Former is null) return null;

        ArticleRevision? Current = await Context.ArticleRevisions
            .AsNoTracking()
            .Where(r => r.CanonicalArticleId == Former.CanonicalArticleId && r.IsCurrent)
            .FirstOrDefaultAsync(CancellationToken);

        return Current is not null && !Current.UrlSlug.Equals(UrlSlug, StringComparison.OrdinalIgnoreCase) ? Current : null;
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

    /// <summary>
    /// Reads and consumes any pending redirect-origin flash state in <see cref="Microsoft.AspNetCore.Mvc.RazorPages.PageModel.TempData"/>
    /// and exposes it when the displayed revision is its intended destination.
    /// </summary>
    /// <param name="Displayed">The revision currently being rendered.</param>
    private void ConsumeRedirectNotice(ArticleRevision Displayed)
    {
        if (TempData is null || !TempData.ContainsKey(RedirectedToSlugTempDataKey))
        {
            return;
        }

        string? ExpectedTargetSlug = TempData[RedirectedToSlugTempDataKey] as string;
        string? FromTitle = TempData[RedirectedFromTitleTempDataKey] as string;

        if (!string.IsNullOrWhiteSpace(Revision)
            || Displayed.Type == ArticleType.Redirect
            || string.IsNullOrWhiteSpace(ExpectedTargetSlug)
            || string.IsNullOrWhiteSpace(FromTitle)
            || !ExpectedTargetSlug.Equals(Displayed.UrlSlug, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        RedirectedFromTitle = FromTitle;
    }

    /// <summary>
    /// Reads and consumes the save warnings left by the editor's save, exposing them only when the displayed
    /// revision is the current revision of the article that was saved.
    /// </summary>
    /// <param name="Displayed">The revision currently being rendered.</param>
    private void ConsumeSaveWarnings(ArticleRevision Displayed)
    {
        if (TempData is null || !TempData.ContainsKey(SaveWarningsTempDataKey))
        {
            return;
        }

        string? SavedSlug = TempData[SaveWarningsSlugTempDataKey] as string;
        if (TempData[SaveWarningsTempDataKey] is not string[] Warnings
            || !string.IsNullOrWhiteSpace(Revision)
            || string.IsNullOrWhiteSpace(SavedSlug)
            || !SavedSlug.Equals(Displayed.UrlSlug, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SaveWarnings = Warnings;
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

