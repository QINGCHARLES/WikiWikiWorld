using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Exercises feed paging, scope and reporting against SQLite.</summary>
public sealed class FirehoseTests
{
	/// <summary>Equal timestamps and newer insertions do not repeat or skip older revisions.</summary>
	[Fact]
	public async Task CursorKeepsStablePagesAndActivityCounts()
	{
		await using SqliteConnection Connection = new("Data Source=:memory:");
		await Connection.OpenAsync();
		await using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		await Context.Database.EnsureCreatedAsync();
		DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
		Guid UserId = Guid.NewGuid();
		Context.Users.Add(new User { Id = UserId, UserName = "Editor", NormalizedUserName = "EDITOR" });
		Guid PageId = Guid.NewGuid();
		for (int i = 0; i < 55; i++) Context.ArticleRevisions.Add(Revision(UserId, PageId, Now.AddHours(-1)));
		await Context.SaveChangesAsync();
		FirehoseService Service = new(Context);
		IQueryable<ArticleRevision> Query = Service.Query(1, "en", null, null, null, null);
		FirehosePage First = await Service.LoadPageAsync(Query, null, 0, default);
		Assert.Equal(50, First.Entries.Count);
		Assert.True(First.HasMore);
		Context.ArticleRevisions.Add(Revision(UserId, PageId, Now));
		await Context.SaveChangesAsync();
		FirehoseEntry Cursor = First.Entries[^1];
		FirehosePage Second = await Service.LoadPageAsync(Query, Cursor.DateCreated, Cursor.Id, default);
		Assert.Equal(5, Second.Entries.Count);
		Assert.False(Second.HasMore);
		Assert.Equal(55, First.Entries.Concat(Second.Entries).Select(r => r.Id).Distinct().Count());
		FirehoseActivity Activity = await Service.LoadActivityAsync(Query, Now, default);
		Assert.Equal(56, Activity.Changes);
		Assert.Equal(56, Activity.Hours.Sum());
		Assert.Equal(1, Activity.Editors);
		Assert.Equal(1, Activity.Pages);
		Assert.Equal(56, Assert.Single(Activity.Popular).Count);
	}

	/// <summary>Filters exclude other sites, languages, deleted revisions and nonmatching editors.</summary>
	[Fact]
	public async Task FiltersAndEmptyActivityRespectScope()
	{
		await using SqliteConnection Connection = new("Data Source=:memory:");
		await Connection.OpenAsync();
		await using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		await Context.Database.EnsureCreatedAsync();
		DateTimeOffset Now = DateTimeOffset.UtcNow;
		Guid UserId = Guid.NewGuid();
		Context.Users.Add(new User { Id = UserId, UserName = "Editor", NormalizedUserName = "EDITOR" });
		ArticleRevision Match = Revision(UserId, Guid.NewGuid(), Now);
		Context.ArticleRevisions.AddRange(Match, Match with { SiteId = 2 }, Match with { Culture = "fr" },
			Match with { DateDeleted = Now }, Match with { Type = ArticleType.File }, Match with { DateCreated = Now.AddDays(-2) });
		await Context.SaveChangesAsync();
		FirehoseService Service = new(Context);
		IQueryable<ArticleRevision> Query = Service.Query(1, "en", ArticleType.Article, "Magazine", "editor", Now.AddDays(-1));
		Assert.Equal(Match.Id, Assert.Single((await Service.LoadPageAsync(Query, null, 0, default)).Entries).Id);
		FirehoseActivity Empty = await Service.LoadActivityAsync(Service.Query(1, "en", null, null, "missing", null), Now, default);
		Assert.Equal(0, Empty.Changes);
		Assert.Equal(0, Empty.LatestId);
		Assert.Empty(Empty.Popular);
		Assert.Equal(24, Empty.Hours.Count);
	}

	/// <summary>Creates a complete revision suitable for the real schema.</summary>
	/// <param name="UserId">The seeded contributor.</param>
	/// <param name="PageId">The shared page identity.</param>
	/// <param name="Created">The revision instant.</param>
	/// <returns>An unsaved English article revision.</returns>
	private static ArticleRevision Revision(Guid UserId, Guid PageId, DateTimeOffset Created) => new()
	{
		CanonicalArticleId = PageId, SiteId = 1, Culture = "en", Title = "Magazine", UrlSlug = "magazine",
		Text = "Body", RevisionReason = "Correction", Type = ArticleType.Article, CreatedByUserId = UserId, DateCreated = Created
	};
}
