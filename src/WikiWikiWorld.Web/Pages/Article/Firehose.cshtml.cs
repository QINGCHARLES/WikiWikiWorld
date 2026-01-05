using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Page model for displaying the "firehose" of recent changes.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public sealed class FirehoseModel(WikiWikiWorldDbContext Context, SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
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
        RecentRevisions = await Context.ArticleRevisions
            .Where(x => x.SiteId == SiteId && x.Culture == Culture)
            .OrderByDescending(x => x.DateCreated)
            .Take(50)
            .ToListAsync(CancellationToken);

        // Fetch user objects for all unique user IDs
        List<Guid> UniqueUserIds = RecentRevisions.Select(Rev => Rev.CreatedByUserId).Distinct().ToList();
        var UserSpec = new UserByIdsSpec(UniqueUserIds);
        IReadOnlyList<User> UserResults = await Context.Users.WithSpecification(UserSpec).ToListAsync(CancellationToken);

        // Populate dictionary with UserId -> User mappings
        Users = UserResults.ToDictionary(u => u.Id, u => u);

        return Page();
    }


}
