using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Services;

/// <summary>A compact revision without article body text.</summary>
/// <param name="Id">The revision ID and timestamp tie-breaker.</param>
/// <param name="Title">The canonical title at this revision.</param>
/// <param name="UrlSlug">The article slug.</param>
/// <param name="Type">The article namespace.</param>
/// <param name="Reason">The editor's change summary.</param>
/// <param name="DateCreated">The creation instant.</param>
/// <param name="UserId">The contributing account.</param>
public sealed record FirehoseEntry(int Id, string Title, string UrlSlug, ArticleType Type,
	string Reason, DateTimeOffset DateCreated, Guid UserId);

/// <summary>Public contributor identity for a feed entry.</summary>
/// <param name="Id">The account ID.</param>
/// <param name="UserName">The public name.</param>
/// <param name="Portrait">The optional portrait file ID.</param>
public sealed record FirehoseContributor(Guid Id, string? UserName, Guid? Portrait);

/// <summary>A bounded page with continuation information.</summary>
/// <param name="Entries">Up to fifty revisions.</param>
/// <param name="Contributors">Public account identities.</param>
/// <param name="HasMore">Whether another page exists.</param>
public sealed record FirehosePage(IReadOnlyList<FirehoseEntry> Entries,
	IReadOnlyDictionary<Guid, FirehoseContributor> Contributors, bool HasMore);

/// <summary>A page ranked by edits in the reporting window.</summary>
/// <param name="Title">Its latest matching title.</param>
/// <param name="UrlSlug">Its latest matching slug.</param>
/// <param name="Type">Its namespace.</param>
/// <param name="Count">Its edit count.</param>
public sealed record FirehosePopularPage(string Title, string UrlSlug, ArticleType Type, int Count);

/// <summary>Real activity for selected filters, independent of the pagination cursor.</summary>
/// <param name="Changes">Changes in the last 24 hours.</param>
/// <param name="Editors">Distinct contributors in the last 24 hours.</param>
/// <param name="Pages">Distinct pages edited in the last 24 hours.</param>
/// <param name="Hours">Twenty-four hourly counts, oldest first.</param>
/// <param name="Popular">Most edited pages in the last seven days.</param>
/// <param name="LatestId">Highest matching revision ID for update checks.</param>
public sealed record FirehoseActivity(int Changes, int Editors, int Pages, IReadOnlyList<int> Hours,
	IReadOnlyList<FirehosePopularPage> Popular, int LatestId);

/// <summary>Queries public revision activity in the resolved site and culture.</summary>
/// <param name="Context">The scoped context; operations are sequential.</param>
public sealed class FirehoseService(WikiWikiWorldDbContext Context)
{
	/// <summary>Applies feed filters without bypassing deletion or tenant filters.</summary>
	/// <param name="SiteId">The resolved site.</param>
	/// <param name="Culture">The resolved language.</param>
	/// <param name="Type">Optional namespace.</param>
	/// <param name="Search">Optional literal title substring.</param>
	/// <param name="Editor">Optional exact public account name.</param>
	/// <param name="Since">Optional inclusive timestamp lower bound.</param>
	/// <returns>An unexecuted filtered revision query.</returns>
	public IQueryable<ArticleRevision> Query(int SiteId, string Culture, ArticleType? Type,
		string? Search, string? Editor, DateTimeOffset? Since)
	{
		IQueryable<ArticleRevision> Query = Context.ArticleRevisions.AsNoTracking()
			.Where(r => r.SiteId == SiteId && r.Culture == Culture);
		if (Type is not null) Query = Query.Where(r => r.Type == Type);
		if (!string.IsNullOrWhiteSpace(Search)) Query = Query.Where(r => r.Title.Contains(Search));
		if (!string.IsNullOrWhiteSpace(Editor))
		{
			string NormalizedEditor = Editor.ToUpperInvariant();
			Query = Query.Where(r => Context.Users.Any(u => u.Id == r.CreatedByUserId && u.NormalizedUserName == NormalizedEditor));
		}
		if (Since is not null) Query = Query.Where(r => r.DateCreated >= Since);
		return Query;
	}

	/// <summary>Loads a bounded page ordered by timestamp and ID, excluding article bodies.</summary>
	/// <param name="Query">The filtered scope.</param>
	/// <param name="Before">An exclusive older-page timestamp.</param>
	/// <param name="BeforeId">The corresponding ID tie-breaker.</param>
	/// <param name="CancellationToken">Cancels database work.</param>
	/// <returns>The page and its contributors.</returns>
	public async Task<FirehosePage> LoadPageAsync(IQueryable<ArticleRevision> Query,
		DateTimeOffset? Before, int BeforeId, CancellationToken CancellationToken)
	{
		if (Before is not null)
			Query = Query.Where(r => r.DateCreated < Before || (r.DateCreated == Before && r.Id < BeforeId));
		List<FirehoseEntry> Entries = await Query.OrderByDescending(r => r.DateCreated).ThenByDescending(r => r.Id)
			.Select(r => new FirehoseEntry(r.Id, r.Title, r.UrlSlug, r.Type, r.RevisionReason, r.DateCreated, r.CreatedByUserId))
			.Take(51).ToListAsync(CancellationToken);
		bool HasMore = Entries.Count > 50;
		if (HasMore) Entries.RemoveAt(50);
		Guid[] UserIds = [.. Entries.Select(r => r.UserId).Distinct()];
		Dictionary<Guid, FirehoseContributor> Contributors = await Context.Users.AsNoTracking()
			.Where(u => UserIds.Contains(u.Id)).Select(u => new FirehoseContributor(u.Id, u.UserName, u.ProfilePicGuid))
			.ToDictionaryAsync(u => u.Id, CancellationToken);
		return new(Entries, Contributors, HasMore);
	}

	/// <summary>Computes measured daily activity and a seven-day ranking; presence is never inferred.</summary>
	/// <param name="Query">The filtered scope without pagination.</param>
	/// <param name="Now">The UTC reporting instant.</param>
	/// <param name="CancellationToken">Cancels database work.</param>
	/// <returns>Counts, hourly distribution and a five-page ranking.</returns>
	public async Task<FirehoseActivity> LoadActivityAsync(IQueryable<ArticleRevision> Query,
		DateTimeOffset Now, CancellationToken CancellationToken)
	{
		DateTimeOffset DayStart = Now.AddHours(-24);
		DateTimeOffset WeekStart = Now.AddDays(-7);
		IQueryable<ArticleRevision> Day = Query.Where(r => r.DateCreated >= DayStart && r.DateCreated <= Now);
		List<DateTimeOffset> Times = await Day.Select(r => r.DateCreated).ToListAsync(CancellationToken);
		int Editors = await Day.Select(r => r.CreatedByUserId).Distinct().CountAsync(CancellationToken);
		int Pages = await Day.Select(r => r.CanonicalArticleId).Distinct().CountAsync(CancellationToken);
		int[] Hours = new int[24];
		foreach (DateTimeOffset Time in Times)
			Hours[Math.Clamp((int)(Time - DayStart).TotalHours, 0, 23)]++;
		var Ranked = await Query.Where(r => r.DateCreated >= WeekStart && r.DateCreated <= Now)
			.GroupBy(r => r.CanonicalArticleId).Select(g => new { Id = g.Key, Count = g.Count() })
			.OrderByDescending(g => g.Count).ThenBy(g => g.Id).Take(5).ToListAsync(CancellationToken);
		List<FirehosePopularPage> Popular = [];
		foreach (var Item in Ranked)
		{
			Popular.Add(await Query.Where(r => r.CanonicalArticleId == Item.Id)
				.OrderByDescending(r => r.DateCreated).ThenByDescending(r => r.Id)
				.Select(r => new FirehosePopularPage(r.Title, r.UrlSlug, r.Type, Item.Count)).FirstAsync(CancellationToken));
		}
		int LatestId = await Query.Select(r => (int?)r.Id).MaxAsync(CancellationToken) ?? 0;
		return new(Times.Count, Editors, Pages, Hours, Popular, LatestId);
	}
}
