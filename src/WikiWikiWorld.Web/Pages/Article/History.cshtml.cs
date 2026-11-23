using System.Globalization;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Web.Helpers;

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

    /// <summary>
    /// Gets the canonical article path for the current article revision.
    /// </summary>
    public string CanonicalArticlePath { get; private set; } = string.Empty;

    // List of all article revisions.
    /// <summary>
    /// Gets the list of revisions for the article.
    /// </summary>
    public IReadOnlyList<ArticleRevision> ArticleRevisions { get; private set; } = [];

    /// <summary>
    /// Gets a dictionary mapping user IDs to usernames for display.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> UserNames { get; private set; } = new Dictionary<Guid, string>();

    /// <summary>
    /// Gets a dictionary mapping user IDs to profile picture GUIDs.
    /// </summary>
    public IReadOnlyDictionary<Guid, Guid?> UserProfilePics { get; private set; } = new Dictionary<Guid, Guid?>();

    /// <summary>
    /// Gets the title of the current revision, or the latest available revision if the article has no current revision.
    /// </summary>
    public string ArticleTitle { get; private set; } = string.Empty;

    /// <summary>
    /// Loads an article's history across slug changes and redirects former addresses to the current history.
    /// </summary>
    /// <param name="CancellationToken">A token that cancels the database reads when the request ends.</param>
    /// <returns>The history page, a redirect to its current address, or an error result for an invalid or missing article.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
    {
		if (SiteId <= 0 || string.IsNullOrWhiteSpace(Culture) || string.IsNullOrWhiteSpace(UrlSlug))
			return BadRequest("Invalid parameters.");

		string RequestedUrlSlug = UrlSlug;
		string LookupUrlSlug = ArticleUrlHelper.NormalizeLookupSlug(UrlSlug);
		UrlSlug = LookupUrlSlug;

		// A current article at this slug takes precedence over any former occupants.
		Guid? CanonicalArticleId = await Context.ArticleRevisions
			.Where(r => r.UrlSlug == LookupUrlSlug)
			.OrderByDescending(r => r.IsCurrent)
			.ThenByDescending(r => r.DateCreated)
			.Select(r => (Guid?)r.CanonicalArticleId)
			.FirstOrDefaultAsync(CancellationToken);

		if (CanonicalArticleId is null)
			return NotFound("No revisions found for this article.");

		ArticleRevisionsByCanonicalIdSpec HistorySpec = new(CanonicalArticleId.Value, MaxDate: null);
		ArticleRevisions = await Context.ArticleRevisions.WithSpecification(HistorySpec).ToListAsync(CancellationToken);

		ArticleRevision? CurrentRevision = ArticleRevisions.FirstOrDefault(r => r.IsCurrent) ?? ArticleRevisions.FirstOrDefault();
		if (CurrentRevision is null)
			return NotFound("No revisions found for this article.");

		if (CurrentRevision.IsCurrent && !CurrentRevision.UrlSlug.Equals(LookupUrlSlug, StringComparison.Ordinal))
			return Redirect(ArticleUrlHelper.BuildRelativePath(CurrentRevision, "history"));

		if (ArticleUrlHelper.RequiresCanonicalRedirect(RequestedUrlSlug, CurrentRevision.Type))
			return RedirectPermanent(ArticleUrlHelper.BuildRelativePath(CurrentRevision, "history"));

		CanonicalArticlePath = ArticleUrlHelper.BuildArticlePath(CurrentRevision);
		ArticleTitle = CurrentRevision.Title;

		List<Guid> UserIds = ArticleRevisions.Select(r => r.CreatedByUserId).Distinct().ToList();
		UserByIdsSpec UserSpec = new(UserIds);
		List<User> Users = await Context.Users.WithSpecification(UserSpec).ToListAsync(CancellationToken);

		UserNames = Users
			.Where(u => u.UserName is not null)
			.ToDictionary(u => u.Id, u => u.UserName!);

		UserProfilePics = Users.ToDictionary(u => u.Id, u => u.ProfilePicGuid);

		return Page();
    }

    /// <summary>
    /// Gets a user-friendly relative time string for a date.
    /// </summary>
    /// <param name="date">The date to format.</param>
    /// <returns>A human-readable relative time string.</returns>
    public static string GetRelativeTime(DateTimeOffset date)
    {
        return WikiWikiWorld.Web.Helpers.TimeAgo.GetDetailedTimeAgoString(DateTimeOffset.UtcNow, date);
    }

    /// <summary>
    /// Converts a DateTimeOffset to the 15-21 digit revision format.
    /// </summary>
    /// <param name="date">The DateTimeOffset to format.</param>
    /// <returns>A string representing the formatted revision timestamp.</returns>
    public static string FormatRevisionTimestamp(DateTimeOffset date)
    {
		string Timestamp = date.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);
		// Seconds are part of every revision permalink even when the fractional part is empty.
		return Timestamp[..14] + Timestamp[14..].TrimEnd('0');
    }
}
