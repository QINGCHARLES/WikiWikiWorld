using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Pages.Profile;

/// <summary>
/// Page model for displaying a user's bookmarked articles. Only the profile owner may view this page.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="UserManager">The user manager.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="BookmarkService">The bookmark service.</param>
/// <param name="ProfileService">Loads the shared profile header.</param>
public sealed class BookmarksModel(
    WikiWikiWorldDbContext Context,
    UserManager<User> UserManager,
    SiteResolverService SiteResolverService,
    BookmarkService BookmarkService,
    ProfileService ProfileService) : BasePageModel(SiteResolverService)
{
    /// <summary>
    /// Gets or sets the username whose bookmarks are being viewed.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets the shared profile header; set before the page renders.
    /// </summary>
    public ProfileHeader Header { get; private set; } = null!;

    /// <summary>
    /// Gets or sets the bookmarked articles, ordered newest first.
    /// </summary>
    public IReadOnlyList<BookmarkListItem> Items { get; set; } = [];

    /// <summary>
    /// Handles the GET request to view the user's bookmarks.
    /// </summary>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>The page, NotFound, or a Forbid response.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
    {
        IActionResult? Failure = await LoadProfileAsync(CancellationToken);
        if (Failure is not null)
        {
            return Failure;
        }

        await LoadBookmarksAsync(CancellationToken);
        return Page();
    }

    /// <summary>
    /// Removes a bookmark owned by the current user.
    /// </summary>
    /// <param name="BookmarkId">The identifier of the bookmark to remove.</param>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>A redirect back to the bookmarks page on success, or an auth/not-found result.</returns>
    public async Task<IActionResult> OnPostRemoveAsync(int BookmarkId, CancellationToken CancellationToken)
    {
        if (!(User.Identity?.IsAuthenticated ?? false))
        {
            return Challenge();
        }

        User? CurrentUser = await UserManager.GetUserAsync(User);
        if (CurrentUser is null)
        {
            return Challenge();
        }

        await BookmarkService.RemoveBookmarkAsync(BookmarkId, CurrentUser.Id, CancellationToken);

        return RedirectToPage(new { Username });
    }

    /// <summary>
    /// Loads the profile header and enforces that only a signed-in owner may see their bookmarks.
    /// </summary>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns><see langword="null"/> when the page may render; otherwise the not-found, challenge, or forbid result.</returns>
    private async Task<IActionResult?> LoadProfileAsync(CancellationToken CancellationToken)
    {
        ProfileHeader? LoadedHeader = await ProfileService.GetHeaderAsync(Username, User, SiteId, CancellationToken);
        if (LoadedHeader is null)
        {
            return NotFound();
        }

        if (!(User.Identity?.IsAuthenticated ?? false))
        {
            return Challenge();
        }

        // Bookmarks are private to their owner.
        if (!LoadedHeader.IsOwner)
        {
            return Forbid();
        }

        Header = LoadedHeader;
        AllowSearchEngineIndexingOfPage = false;
        return null;
    }

    private async Task LoadBookmarksAsync(CancellationToken CancellationToken)
    {
        User? CurrentUser = await UserManager.GetUserAsync(User);
        if (CurrentUser is null)
        {
            return;
        }

        IReadOnlyList<Bookmark> Bookmarks = await BookmarkService.GetUserBookmarksAsync(CurrentUser.Id, CancellationToken);
        if (Bookmarks.Count == 0)
        {
            return;
        }

        HashSet<Guid> ArticleIds = [.. Bookmarks.Select(b => b.CanonicalArticleId)];

        // Resolve current revision per canonical id in a single query.
        Dictionary<Guid, ArticleRevision> CurrentRevisions = await Context.ArticleRevisions
            .AsNoTracking()
            .Where(r => ArticleIds.Contains(r.CanonicalArticleId) && r.IsCurrent)
            .ToDictionaryAsync(r => r.CanonicalArticleId, CancellationToken);

        List<BookmarkListItem> Result = new(Bookmarks.Count);
        foreach (Bookmark Bookmark in Bookmarks)
        {
            if (!CurrentRevisions.TryGetValue(Bookmark.CanonicalArticleId, out ArticleRevision? Revision))
            {
                // Article was deleted or is from a different site; skip silently.
                continue;
            }

            Result.Add(new BookmarkListItem(
                Bookmark.Id,
                Revision.Title,
                ArticleUrlHelper.BuildArticlePath(Revision),
                Bookmark.DateCreated));
        }

        Items = Result;
    }

    /// <summary>
    /// View-model row for a single bookmarked article.
    /// </summary>
    /// <param name="BookmarkId">The bookmark identifier (used by the remove form).</param>
    /// <param name="Title">The current article title.</param>
    /// <param name="ArticlePath">The current article URL path.</param>
    /// <param name="DateBookmarked">When the bookmark was created.</param>
    public sealed record BookmarkListItem(int BookmarkId, string Title, string ArticlePath, DateTimeOffset DateBookmarked);
}
