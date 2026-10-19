using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// View model passed to the <c>_FirehoseRows</c> partial, pairing a page of
/// revisions with the users who created them.
/// </summary>
/// <param name="Revisions">The revisions to render as table rows.</param>
/// <param name="Users">A lookup of user id to user for the revisions.</param>
public sealed record FirehoseRowsViewModel(
    IReadOnlyList<ArticleRevision> Revisions,
    IReadOnlyDictionary<Guid, User> Users);

/// <summary>
/// Page model for displaying the "firehose" of recent changes.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public sealed class FirehoseModel(WikiWikiWorldDbContext Context, SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    /// <summary>
    /// The number of revisions rendered on the initial page load.
    /// </summary>
    public const int InitialCount = 50;

    /// <summary>
    /// The number of additional revisions fetched each time "Load more" is clicked.
    /// </summary>
    public const int LoadMoreCount = 50;

    /// <summary>
    /// Gets the list of recent article revisions.
    /// </summary>
    public IReadOnlyList<ArticleRevision> RecentRevisions { get; private set; } = [];

    /// <summary>
    /// Gets the dictionary of users associated with the revisions.
    /// </summary>
    public Dictionary<Guid, User> Users { get; private set; } = [];

    /// <summary>
    /// Handles the GET request to view recent changes.
    /// </summary>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>The page.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
    {
        (RecentRevisions, Users) = await LoadRevisionsAsync(0, InitialCount, CancellationToken);

        return Page();
    }

    /// <summary>
    /// Handles the AJAX request for the next page of revisions, returning the
    /// rendered table rows for the "Load more" button to append.
    /// </summary>
    /// <param name="Skip">The number of revisions already shown.</param>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>The rendered rows partial.</returns>
    public async Task<IActionResult> OnGetRowsAsync(int Skip, CancellationToken CancellationToken)
    {
        if (Skip < 0)
        {
            Skip = 0;
        }

        (IReadOnlyList<ArticleRevision> Revisions, Dictionary<Guid, User> Users) =
            await LoadRevisionsAsync(Skip, LoadMoreCount, CancellationToken);

        return Partial("_FirehoseRows", new FirehoseRowsViewModel(Revisions, Users));
    }

    /// <summary>
    /// Loads a page of recent revisions for the current site and culture, along
    /// with the users who created them.
    /// </summary>
    /// <param name="Skip">The number of revisions to skip.</param>
    /// <param name="Take">The maximum number of revisions to return.</param>
    /// <param name="CancellationToken">A cancellation token.</param>
    /// <returns>The revisions and their associated users.</returns>
    private async Task<(IReadOnlyList<ArticleRevision> Revisions, Dictionary<Guid, User> Users)> LoadRevisionsAsync(
        int Skip,
        int Take,
        CancellationToken CancellationToken)
    {
        IReadOnlyList<ArticleRevision> Revisions = await Context.ArticleRevisions
            .Where(x => x.SiteId == SiteId && x.Culture == Culture)
            .OrderByDescending(x => x.DateCreated)
            .Skip(Skip)
            .Take(Take)
            .ToListAsync(CancellationToken);

        // Fetch user objects for all unique user IDs
        List<Guid> UniqueUserIds = Revisions.Select(Rev => Rev.CreatedByUserId).Distinct().ToList();
        var UserSpec = new UserByIdsSpec(UniqueUserIds);
        IReadOnlyList<User> UserResults = await Context.Users.WithSpecification(UserSpec).ToListAsync(CancellationToken);

        // Populate dictionary with UserId -> User mappings
        Dictionary<Guid, User> Users = UserResults.ToDictionary(u => u.Id, u => u);

        return (Revisions, Users);
    }
}
