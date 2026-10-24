using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Exercises list visibility, bounded queries, and mutations against actual SQLite.</summary>
public sealed class BookmarkManagementTests : IDisposable
{
	private readonly SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
	private readonly Guid Owner = Guid.NewGuid();
	private readonly Guid Other = Guid.NewGuid();

	/// <summary>Creates an isolated schema and two owners for permission checks.</summary>
	public BookmarkManagementTests()
	{
		Connection.Open();
		using WikiWikiWorldDbContext Context = CreateContext();
		Context.Database.EnsureCreated();
		Context.Users.AddRange(new User { Id = Owner, UserName = "Owner" }, new User { Id = Other, UserName = "Other" });
		Context.SaveChanges();
	}

	/// <inheritdoc/>
	public void Dispose() => Connection.Dispose();

	/// <summary>Four hundred equal-date saves have stable, disjoint pages, with paging performed after visibility filtering.</summary>
	[Fact]
	public async Task FourHundredEntriesHaveEightStablePages()
	{
		int ListId = await SeedAsync(400);
		await using WikiWikiWorldDbContext Context = CreateContext(1);
		BookmarkService Service = new(Context);
		HashSet<int> Seen = [];
		for (int i = 1; i <= 8; i++)
		{
			BookmarkEntryPage Page = await Service.GetListPageAsync(ListId, Owner, true, "recent", i, default);
			Assert.Equal(400, Page.TotalCount);
			Assert.Equal(8, Page.PageCount);
			Assert.Equal(50, Page.Items.Count);
			Assert.All(Page.Items, b => Assert.True(Seen.Add(b.Id)));
			Assert.Equal(Page.Items.OrderByDescending(b => b.Id), Page.Items);
		}
		Assert.Equal(400, Seen.Count);
		Assert.Equal(8, (await Service.GetListPageAsync(ListId, Owner, true, "recent", int.MaxValue, default)).PageNumber);
		Assert.Equal(1, (await Service.GetListPageAsync(ListId, Owner, true, "recent", -3, default)).PageNumber);
	}

	/// <summary>Unicode case-insensitive sorting is translated to SQLite before paging, with a stable ID tie-break.</summary>
	[Fact]
	public async Task NamesUseUnicodeCaseInsensitiveOrdering()
	{
		int ListId = await SeedAsync(0);
		string[] Titles = ["zebra", "Éclair", "éclair", "Alpha", "beta", "日本"];
		await using (WikiWikiWorldDbContext Context = CreateContext())
		{
			foreach (string Title in Titles)
				AddEntry(Context, ListId, Title, Owner);
			await Context.SaveChangesAsync();
		}
		await using WikiWikiWorldDbContext Read = CreateContext(1);
		BookmarkEntryPage Page = await new BookmarkService(Read).GetListPageAsync(ListId, Owner, true, "name", 1, default);
		Assert.Equal(Titles.Order(StringComparer.InvariantCultureIgnoreCase), Page.Items.Select(b => b.Title));
		Assert.True(Page.Items.First(b => b.Title == "Éclair").Id < Page.Items.First(b => b.Title == "éclair").Id);
	}

	/// <summary>Private and foreign-site lists, deleted/missing articles, and other cultures never leak into public results or counts.</summary>
	[Fact]
	public async Task VisibilityAndScopeApplyBeforeCounting()
	{
		int PrivateId = await SeedAsync(1);
		await using (WikiWikiWorldDbContext Seed = CreateContext())
		{
			BookmarkList Public = new() { SiteId = 1, Name = "Public", IsPublic = true, CreatedByUserId = Owner };
			BookmarkList Foreign = new() { SiteId = 2, Name = "Foreign secret", IsPublic = true, CreatedByUserId = Owner };
			Seed.BookmarkLists.AddRange(Public, Foreign);
			await Seed.SaveChangesAsync();
			AddEntry(Seed, Public.Id, "Visible", Owner);
			AddEntry(Seed, Public.Id, "Other culture", Owner, Culture: "fr");
			AddEntry(Seed, Public.Id, "Deleted article", Owner, Deleted: true);
			Seed.Bookmarks.Add(new Bookmark { BookmarkListId = Public.Id, CreatedByUserId = Owner, SiteId = 1, CanonicalArticleId = Guid.NewGuid() });
			await Seed.SaveChangesAsync();
		}
		await using WikiWikiWorldDbContext Read = CreateContext(1);
		BookmarkService Service = new(Read);
		BookmarkListSummary Visible = Assert.Single(await Service.GetVisibleListsAsync(Owner, false, default));
		Assert.Equal("Public", Visible.Name);
		Assert.Equal(1, Visible.Count);
		Assert.Single((await Service.GetListPageAsync(Visible.Id, Owner, false, "recent", 1, default)).Items);
		Assert.Empty((await Service.GetListPageAsync(PrivateId, Owner, false, "recent", 1, default)).Items);
		Assert.Equal(2, (await Service.GetVisibleListsAsync(Owner, true, default)).Count);
	}

	/// <summary>Creating empty lists reserves the default name, trims input, enforces limits, and starts private unless selected.</summary>
	[Fact]
	public async Task CreationValidatesNamesAndDefaultDestination()
	{
		await using WikiWikiWorldDbContext Context = CreateContext(1);
		BookmarkService Service = new(Context);
		Assert.Equal(0, Assert.Single(await Service.GetVisibleListsAsync(Owner, true, default)).Id);
		BookmarkListCreation Created = await Service.CreateEmptyListAsync("  Travel  ", false, Owner, 1, default);
		Assert.NotNull(Created.ListId);
		Assert.NotNull((await Service.CreateEmptyListAsync("travel", true, Owner, 1, default)).Error);
		Assert.NotNull((await Service.CreateEmptyListAsync("Bookmarks", false, Owner, 1, default)).Error);
		Assert.NotNull((await Service.CreateEmptyListAsync(" ", false, Owner, 1, default)).Error);
		Assert.NotNull((await Service.CreateEmptyListAsync(new string('x', 61), false, Owner, 1, default)).Error);
		Assert.Empty(await Service.GetVisibleListsAsync(Owner, false, default));
		Assert.Equal(2, (await Service.GetVisibleListsAsync(Owner, true, default)).Count);
		for (int i = 0; i < 98; i++)
			Context.BookmarkLists.Add(new() { Name = $"List {i}", SiteId = 1, CreatedByUserId = Owner });
		await Context.SaveChangesAsync();
		Assert.NotNull((await Service.CreateEmptyListAsync("Over limit", false, Owner, 1, default)).Error);
	}

	/// <summary>Only the owner can publish, remove, restore, or delete a selected list.</summary>
	[Fact]
	public async Task MutationsRejectOtherOwnersAndSites()
	{
		int ListId = await SeedAsync(1);
		await using WikiWikiWorldDbContext Context = CreateContext(1);
		BookmarkService Service = new(Context);
		int EntryId = await Context.Bookmarks.Select(b => b.Id).SingleAsync();
		Assert.False(await Service.SetListVisibilityAsync(ListId, true, Other, 1, default));
		Assert.Null(await Service.RemoveEntryAsync(ListId, EntryId, Other, default));
		Assert.False(await Service.DeleteOrClearListAsync(ListId, Other, default));
		Assert.False(await Service.RestoreEntryAsync(ListId, EntryId, DateTimeOffset.UtcNow, Other, default));
		Assert.True(await Service.SetListVisibilityAsync(ListId, true, Owner, 1, default));
		Assert.Single(await Service.GetVisibleListsAsync(Owner, false, default));
		await using WikiWikiWorldDbContext Foreign = CreateContext(2);
		Assert.False(await new BookmarkService(Foreign).SetListVisibilityAsync(ListId, true, Owner, 2, default));
		Assert.Empty(await new BookmarkService(Foreign).GetVisibleListsAsync(Owner, false, default));
	}

	/// <summary>Removal only affects the selected membership; undo retains the save date and clear invalidates old undo commands.</summary>
	[Fact]
	public async Task RemovalUndoAndClearPreserveOtherLists()
	{
		int ListId = await SeedAsync(1);
		int OtherList;
		Bookmark Original;
		await using (WikiWikiWorldDbContext Setup = CreateContext(1))
		{
			Original = await Setup.Bookmarks.AsNoTracking().SingleAsync();
			BookmarkService Service = new(Setup);
			OtherList = (await Service.CreateEmptyListAsync("Second", false, Owner, 1, default)).ListId!.Value;
			Assert.True(await Service.SetBookmarkInListAsync(OtherList, Original.CanonicalArticleId, Owner, 1, true, default));
		}
		await using (WikiWikiWorldDbContext Context = CreateContext(1))
		{
			BookmarkService Service = new(Context);
			DateTimeOffset Removed = (await Service.RemoveEntryAsync(ListId, Original.Id, Owner, default))!.Value;
			Assert.Single((await Service.GetListPageAsync(OtherList, Owner, true, "recent", 1, default)).Items);
			Assert.True(await Service.RestoreEntryAsync(ListId, Original.Id, Removed, Owner, default));
			Assert.Equal(Original.DateCreated, (await Service.GetListPageAsync(ListId, Owner, true, "recent", 1, default)).Items[0].DateCreated);
		}
		await using (WikiWikiWorldDbContext Context = CreateContext(1))
		{
			BookmarkService Service = new(Context);
			DateTimeOffset Removed = (await Service.RemoveEntryAsync(ListId, Original.Id, Owner, default))!.Value;
			Assert.True(await Service.DeleteOrClearListAsync(ListId, Owner, default));
			Assert.False(await Service.RestoreEntryAsync(ListId, Original.Id, Removed, Owner, default));
			Assert.True(await Context.BookmarkLists.AnyAsync(l => l.Id == ListId));
			Assert.Single((await Service.GetListPageAsync(OtherList, Owner, true, "recent", 1, default)).Items);
			Assert.True(await Service.DeleteOrClearListAsync(OtherList, Owner, default));
			Assert.False(await Context.BookmarkLists.AnyAsync(l => l.Id == OtherList));
			Assert.Empty(await Context.Bookmarks.ToListAsync());
			Assert.False(await Service.SetBookmarkInListAsync(OtherList, Original.CanonicalArticleId, Owner, 1, true, default));
		}
	}

	/// <summary>Removing the only entry on the last page clamps to the previous page.</summary>
	[Fact]
	public async Task LastPageRemovalClampsPage()
	{
		int ListId = await SeedAsync(51);
		await using WikiWikiWorldDbContext Context = CreateContext(1);
		BookmarkService Service = new(Context);
		BookmarkEntryPage Last = await Service.GetListPageAsync(ListId, Owner, true, "recent", 2, default);
		await Service.RemoveEntryAsync(ListId, Assert.Single(Last.Items).Id, Owner, default);
		Assert.Equal(1, (await Service.GetListPageAsync(ListId, Owner, true, "recent", 2, default)).PageNumber);
	}

	/// <summary>The additive upgrade preserves names and memberships and keeps all old lists private.</summary>
	[Fact]
	public void SchemaUpgradePreservesExistingData()
	{
		using SqliteConnection Old = new("Data Source=:memory:");
		Old.Open();
		using SqliteCommand Command = Old.CreateCommand();
		Command.CommandText = """
			CREATE TABLE BookmarkLists (Id INTEGER PRIMARY KEY, Name TEXT, IsDefault INTEGER);
			CREATE TABLE Bookmarks (Id INTEGER PRIMARY KEY, BookmarkListId INTEGER, DateCreated TEXT, DateDeleted TEXT);
			INSERT INTO BookmarkLists VALUES (1, 'Existing', 1);
			INSERT INTO Bookmarks VALUES (1, 1, '2026-01-01', NULL);
			""";
		Command.ExecuteNonQuery();
		Command.CommandText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts/AddBookmarkListVisibility.sql"));
		Command.ExecuteNonQuery();
		Command.CommandText = "SELECT Name || ':' || IsPublic FROM BookmarkLists WHERE Id = 1";
		Assert.Equal("Existing:0", Command.ExecuteScalar());
		Command.CommandText = "SELECT COUNT(*) FROM Bookmarks";
		Assert.Equal(1L, Command.ExecuteScalar());
	}

	/// <summary>Seeds a default list with equal-date memberships to exercise stable tie-breaking.</summary>
	/// <param name="Count">Number of current English articles.</param>
	/// <returns>The saved list ID.</returns>
	private async Task<int> SeedAsync(int Count)
	{
		await using WikiWikiWorldDbContext Context = CreateContext();
		BookmarkList List = new() { SiteId = 1, Name = "Bookmarks", IsDefault = true, CreatedByUserId = Owner };
		Context.BookmarkLists.Add(List);
		await Context.SaveChangesAsync();
		for (int i = 0; i < Count; i++)
			AddEntry(Context, List.Id, $"Article {i:D4}", Owner);
		await Context.SaveChangesAsync();
		return List.Id;
	}

	/// <summary>Adds an unsaved article and membership with a fixed UTC save date.</summary>
	/// <param name="Context">Fixture context.</param>
	/// <param name="ListId">Owning list.</param>
	/// <param name="Title">Article title.</param>
	/// <param name="UserId">Owner and author.</param>
	/// <param name="Culture">Article language.</param>
	/// <param name="Deleted">Whether the article has been soft-deleted.</param>
	private static void AddEntry(WikiWikiWorldDbContext Context, int ListId, string Title, Guid UserId, string Culture = "en", bool Deleted = false)
	{
		Guid Article = Guid.NewGuid();
		DateTimeOffset Date = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		Context.ArticleRevisions.Add(new()
		{
			CanonicalArticleId = Article, SiteId = 1, Culture = Culture, Title = Title, UrlSlug = Article.ToString(),
			Text = "", RevisionReason = "Test", IsCurrent = true, CreatedByUserId = UserId, DateCreated = Date,
			DateDeleted = Deleted ? Date : null
		});
		Context.Bookmarks.Add(new() { BookmarkListId = ListId, SiteId = 1, CanonicalArticleId = Article, CreatedByUserId = UserId, DateCreated = Date });
	}

	/// <summary>Creates an independent unit of work on the shared disposable connection.</summary>
	/// <param name="SiteId">Optional query-filter scope.</param>
	/// <returns>A context the caller disposes.</returns>
	private WikiWikiWorldDbContext CreateContext(int? SiteId = null)
	{
		DbContextOptions<WikiWikiWorldDbContext> Options = new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection, Options => Options.ExecutionStrategy(Dependencies => new SqliteRetryExecutionStrategy(Dependencies))).Options;
		return SiteId.HasValue ? new(Options, new FixedSiteContext(SiteId.Value)) : new(Options);
	}

	/// <summary>Scopes test queries to one site and English.</summary>
	/// <param name="SiteId">Current site.</param>
	private sealed class FixedSiteContext(int SiteId) : ISiteContextService
	{
		/// <inheritdoc/>
		public int? GetCurrentSiteId() => SiteId;
		/// <inheritdoc/>
		public string? GetCurrentCulture() => "en";
	}
}
