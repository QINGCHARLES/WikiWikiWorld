using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Exercises Markdown list introductions, permissions, lifecycle behavior and the manual SQLite upgrade.</summary>
public sealed class BookmarkListDescriptionTests : IDisposable
{
	private readonly SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
	private readonly Guid Owner = Guid.NewGuid();
	private readonly Guid Other = Guid.NewGuid();

	/// <summary>Creates an isolated provider-backed schema with two owners and the application's retry strategy.</summary>
	public BookmarkListDescriptionTests()
	{
		Connection.Open();
		using WikiWikiWorldDbContext Context = CreateContext();
		Context.Database.EnsureCreated();
		Context.Users.AddRange(new User { Id = Owner, UserName = "Owner" }, new User { Id = Other, UserName = "Other" });
		Context.SaveChanges();
	}

	/// <inheritdoc/>
	public void Dispose() => Connection.Dispose();

	/// <summary>Creation stores exact Markdown independently of entries, and edits change only the introduction and modification time.</summary>
	[Fact]
	public async Task CreationAndEditingPreserveMarkdownAndListMetadata()
	{
		const string Source = "# Why this list\n\n  *Research* and [archives](https://example.com)  \n日本語\n";
		await using WikiWikiWorldDbContext Context = CreateContext();
		BookmarkService Service = new(Context);
		BookmarkListCreation Created = await Service.CreateEmptyListAsync("  Reading  ", true, Owner, 1, Source, default);
		Assert.Null(Created.Error);
		int ListId = Created.ListId!.Value;
		BookmarkList Original = await Context.BookmarkLists.AsNoTracking().SingleAsync(l => l.Id == ListId);
		Assert.Null(Original.DateModified);
		BookmarkListDetails Details = Assert.IsType<BookmarkListDetails>(await Service.GetListDetailsAsync(ListId, Owner, false, default));
		Assert.Equal(Source, Details.Description);
		Assert.Equal("Reading", Details.Name);
		Assert.True(Details.IsPublic);
		Assert.Empty((await Service.GetListPageAsync(ListId, Owner, false, "recent", 1, default)).Items);

		DateTimeOffset Before = DateTimeOffset.UtcNow;
		Assert.True(await Service.SetListDescriptionAsync(ListId, Source + "\n**Updated**", Owner, 1, default));
		BookmarkList Updated = await Context.BookmarkLists.AsNoTracking().SingleAsync(l => l.Id == ListId);
		Assert.Equal(Source + "\n**Updated**", Updated.Description);
		Assert.InRange(Updated.DateModified!.Value, Before, DateTimeOffset.UtcNow);
		Assert.Equal(Original.DateCreated, Updated.DateCreated);
		Assert.Equal(Original.Name, Updated.Name);
		Assert.Equal(Original.IsPublic, Updated.IsPublic);
		Assert.Equal(Owner, Updated.CreatedByUserId);
		await using WikiWikiWorldDbContext French = CreateContext(Culture: "fr");
		Assert.Equal(Updated.Description, (await new BookmarkService(French).GetListDetailsAsync(ListId, Owner, false, default))!.Description);
		Assert.Equal(BookmarkList.MaxDescriptionLength, Context.Model.FindEntityType(typeof(BookmarkList))!.FindProperty(nameof(BookmarkList.Description))!.GetMaxLength());
	}

	/// <summary>Both creation and updates represent absence consistently while preserving other source verbatim.</summary>
	/// <param name="Blank">A supported form of an absent introduction.</param>
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" \t\r\n")]
	public async Task BlankDescriptionsBecomeNull(string? Blank)
	{
		await using WikiWikiWorldDbContext Context = CreateContext();
		BookmarkService Service = new(Context);
		int ListId = (await Service.CreateEmptyListAsync("Empty", false, Owner, 1, Blank, default)).ListId!.Value;
		Assert.Null((await Service.GetListDetailsAsync(ListId, Owner, true, default))!.Description);
		Assert.True(await Service.SetListDescriptionAsync(ListId, "Meaningful", Owner, 1, default));
		Assert.True(await Service.SetListDescriptionAsync(ListId, Blank, Owner, 1, default));
		Assert.Null((await Service.GetListDetailsAsync(ListId, Owner, true, default))!.Description);
	}

	/// <summary>Oversized descriptions fail before default creation or writes; the exact limit is accepted on creation and editing.</summary>
	[Fact]
	public async Task DescriptionLengthIsValidatedBeforeWriting()
	{
		await using WikiWikiWorldDbContext Context = CreateContext();
		BookmarkService Service = new(Context);
		string TooLong = new('x', BookmarkList.MaxDescriptionLength + 1);
		Assert.NotNull((await Service.CreateEmptyListAsync("Invalid", false, Owner, 1, TooLong, default)).Error);
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service.SetListDescriptionAsync(0, TooLong, Owner, 1, default));
		Assert.Empty(await Context.BookmarkLists.ToListAsync());

		string AtLimit = new('x', BookmarkList.MaxDescriptionLength);
		int ListId = (await Service.CreateEmptyListAsync("Valid", false, Owner, 1, AtLimit, default)).ListId!.Value;
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service.SetListDescriptionAsync(ListId, TooLong, Owner, 1, default));
		Assert.Equal(AtLimit, (await Service.GetListDetailsAsync(ListId, Owner, true, default))!.Description);
		string Updated = new('y', BookmarkList.MaxDescriptionLength);
		Assert.True(await Service.SetListDescriptionAsync(ListId, Updated, Owner, 1, default));
		Assert.Equal(Updated, (await Service.GetListDetailsAsync(ListId, Owner, true, default))!.Description);
		Assert.Equal(2, await Context.BookmarkLists.CountAsync());
	}

	/// <summary>Default reads do not create state, and the first edit creates a private default list without a bookmark.</summary>
	[Fact]
	public async Task DefaultDescriptionCreatesItsListOnDemand()
	{
		await using WikiWikiWorldDbContext Context = CreateContext();
		BookmarkService Service = new(Context);
		Assert.Equal(0, (await Service.GetListDetailsAsync(0, Owner, true, default))!.Id);
		Assert.Null(await Service.GetListDetailsAsync(0, Owner, false, default));
		Assert.Empty(await Context.BookmarkLists.ToListAsync());
		Assert.True(await Service.SetListDescriptionAsync(0, "*My reading list*", Owner, 1, default));
		BookmarkListDetails Details = (await Service.GetListDetailsAsync(0, Owner, true, default))!;
		Assert.True(Details.Id > 0);
		Assert.True(Details.IsDefault);
		Assert.False(Details.IsPublic);
		Assert.Equal("*My reading list*", Details.Description);
		Assert.Empty(await Context.Bookmarks.ToListAsync());
		Assert.Single(await Context.BookmarkLists.ToListAsync());
	}

	/// <summary>Public access follows visibility immediately, while foreign owners, sites, missing and deleted lists remain inaccessible.</summary>
	[Fact]
	public async Task ReadsAndWritesRespectOwnershipSiteAndVisibility()
	{
		await using WikiWikiWorldDbContext Context = CreateContext();
		BookmarkService Service = new(Context);
		int ListId = (await Service.CreateEmptyListAsync("Private", false, Owner, 1, "Private introduction", default)).ListId!.Value;
		Assert.Null(await Service.GetListDetailsAsync(ListId, Owner, false, default));
		Assert.Null(await Service.GetListDetailsAsync(ListId, Other, true, default));
		Assert.Null(await Service.GetListDetailsAsync(int.MaxValue, Owner, true, default));
		Assert.False(await Service.SetListDescriptionAsync(ListId, "Overwrite", Other, 1, default));
		Assert.False(await Service.SetListDescriptionAsync(int.MaxValue, "Missing", Owner, 1, default));
		Assert.True(await Service.SetListVisibilityAsync(ListId, true, Owner, 1, default));
		Assert.Equal("Private introduction", (await Service.GetListDetailsAsync(ListId, Owner, false, default))!.Description);
		await using (WikiWikiWorldDbContext Foreign = CreateContext(2))
		{
			BookmarkService ForeignService = new(Foreign);
			Assert.Null(await ForeignService.GetListDetailsAsync(ListId, Owner, true, default));
			Assert.Null(await ForeignService.GetListDetailsAsync(ListId, Owner, false, default));
			Assert.False(await ForeignService.SetListDescriptionAsync(ListId, "Foreign", Owner, 2, default));
		}
		Assert.True(await Service.SetListVisibilityAsync(ListId, false, Owner, 1, default));
		Assert.Null(await Service.GetListDetailsAsync(ListId, Owner, false, default));
		Assert.True(await Service.DeleteOrClearListAsync(ListId, Owner, default));
		Assert.Null(await Service.GetListDetailsAsync(ListId, Owner, true, default));
		Assert.False(await Service.SetListDescriptionAsync(ListId, "Deleted", Owner, 1, default));
		Assert.Equal("Private introduction", await Context.BookmarkLists.IgnoreQueryFilters([WikiWikiWorldDbContext.SoftDeleteFilter])
			.Where(l => l.Id == ListId).Select(l => l.Description).SingleAsync());
	}

	/// <summary>Membership saves, duplicate-name reuse, undo and default clearing preserve each list's independent introduction.</summary>
	[Fact]
	public async Task MembershipOperationsAndClearRetainDescriptions()
	{
		await using WikiWikiWorldDbContext Context = CreateContext();
		BookmarkService Service = new(Context);
		Guid ArticleId = Guid.NewGuid();
		Assert.True(await Service.SetListDescriptionAsync(0, "Default introduction", Owner, 1, default));
		int DefaultId = (await Service.GetListDetailsAsync(0, Owner, true, default))!.Id;
		int CustomId = (await Service.CreateEmptyListAsync("Reading", false, Owner, 1, "Custom introduction", default)).ListId!.Value;
		Assert.NotNull((await Service.CreateEmptyListAsync("reading", true, Owner, 1, "Replacement", default)).Error);
		Assert.Equal(BookmarkListCreateResult.AddedToExisting, await Service.CreateListWithBookmarkAsync("reading", ArticleId, Owner, 1, default));
		await Service.SetBookmarkAsync(ArticleId, Owner, 1, true, default);
		int EntryId = await Context.Bookmarks.Where(b => b.BookmarkListId == DefaultId).Select(b => b.Id).SingleAsync();
		DateTimeOffset RemovedAt = (await Service.RemoveEntryAsync(DefaultId, EntryId, Owner, default))!.Value;
		Assert.True(await Service.RestoreEntryAsync(DefaultId, EntryId, RemovedAt, Owner, default));
		Assert.True(await Service.DeleteOrClearListAsync(DefaultId, Owner, default));
		Assert.Equal("Default introduction", (await Service.GetListDetailsAsync(DefaultId, Owner, true, default))!.Description);
		Assert.Equal("Custom introduction", (await Service.GetListDetailsAsync(CustomId, Owner, true, default))!.Description);
		Assert.False(await Context.Bookmarks.AnyAsync(b => b.BookmarkListId == DefaultId));
		Assert.True(await Context.Bookmarks.AnyAsync(b => b.BookmarkListId == CustomId));
	}

	/// <summary>Authorized stored descriptions use the shared renderer's formatting and restrictions without rewriting their source.</summary>
	[Fact]
	public async Task StoredDescriptionUsesSharedMarkdownPolicy()
	{
		const string Source = "# Reading\n\n*Italic* **Bold** [Archive](https://example.com)\n\n<script>alert(1)</script>\n\n[Unsafe](javascript:alert(1))\n\n{{Stub}}";
		await using WikiWikiWorldDbContext Context = CreateContext();
		BookmarkService Service = new(Context);
		int ListId = (await Service.CreateEmptyListAsync("Markdown", true, Owner, 1, Source, default)).ListId!.Value;
		BookmarkListDetails Details = (await Service.GetListDetailsAsync(ListId, Owner, false, default))!;
		MarkdownPipelineFactory Factory = new(Options.Create(new SiteConfiguration()));
		using IDocument Document = new HtmlParser().ParseDocument(Factory.RenderUserContentMarkdown(Details.Description));
		Assert.Equal("Reading", Document.QuerySelector("h1")!.TextContent);
		Assert.Equal("Italic", Document.QuerySelector("em")!.TextContent);
		Assert.Equal("Bold", Document.QuerySelector("strong")!.TextContent);
		Assert.Equal("https://example.com", Document.QuerySelector("a")!.GetAttribute("href"));
		Assert.Equal(string.Empty, Document.QuerySelectorAll("a")[1].GetAttribute("href"));
		Assert.Null(Document.QuerySelector("script"));
		Assert.Contains("{{Stub}}", Document.Body!.TextContent, StringComparison.Ordinal);
		Assert.Equal(Source, Details.Description);
	}

	/// <summary>Cancellation does not report a successful default-list edit or leave a partially created list behind.</summary>
	[Fact]
	public async Task CancelledDefaultEditDoesNotCreateState()
	{
		await using WikiWikiWorldDbContext Context = CreateContext();
		using CancellationTokenSource Cancellation = new();
		Cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BookmarkService(Context)
			.SetListDescriptionAsync(0, "Cancelled", Owner, 1, Cancellation.Token));
		Assert.Empty(await Context.BookmarkLists.ToListAsync());
	}

	/// <summary>The actual additive script retains all legacy list and membership values and matches the fresh column definition.</summary>
	[Fact]
	public void SchemaUpgradePreservesRowsAndMatchesFreshSchema()
	{
		string FreshSql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts/Create.sql"))
			.Replace("\r\n", "\n", StringComparison.Ordinal);
		const string DescriptionColumn = "    Description TEXT NULL,\n";
		Assert.Contains(DescriptionColumn, FreshSql, StringComparison.Ordinal);
		using SqliteConnection Legacy = new("Data Source=:memory:;Foreign Keys=True");
		Legacy.Open();
		ExecuteSql(Legacy, FreshSql.Replace(DescriptionColumn, "", StringComparison.Ordinal));
		ExecuteSql(Legacy, """
			INSERT INTO Users (Id, EmailConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, DateCreated)
			VALUES ('owner', 0, 0, 0, 0, '2026-01-01');
			INSERT INTO BookmarkLists (Id, SiteId, Name, IsDefault, IsPublic, CreatedByUserId, DateCreated, DateModified, DateDeleted)
			VALUES (1, 1, 'Default café 日本語', 1, 0, 'owner', '2026-01-01', NULL, NULL),
			       (2, 1, 'Public', 0, 1, 'owner', '2026-01-02', '2026-01-03', NULL),
			       (3, 2, 'Deleted', 0, 0, 'owner', '2026-01-04', NULL, '2026-01-05');
			INSERT INTO Bookmarks (Id, BookmarkListId, SiteId, CanonicalArticleId, CreatedByUserId, DateCreated, DateDeleted)
			VALUES (1, 1, 1, 'article', 'owner', '2026-01-02', NULL),
			       (2, 2, 1, 'article', 'owner', '2026-01-03', '2026-01-04');
			""");
		const string ListsQuery = "SELECT Id, SiteId, Name, IsDefault, IsPublic, CreatedByUserId, DateCreated, DateModified, DateDeleted FROM BookmarkLists ORDER BY Id";
		const string EntriesQuery = "SELECT Id, BookmarkListId, SiteId, CanonicalArticleId, CreatedByUserId, DateCreated, DateDeleted FROM Bookmarks ORDER BY Id";
		const string IndexesQuery = "SELECT name, sql FROM sqlite_schema WHERE type = 'index' ORDER BY name";
		IReadOnlyList<object[]> ListsBefore = ReadRows(Legacy, ListsQuery);
		IReadOnlyList<object[]> EntriesBefore = ReadRows(Legacy, EntriesQuery);
		IReadOnlyList<object[]> IndexesBefore = ReadRows(Legacy, IndexesQuery);
		string Precheck = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts/CheckBookmarkListDescriptionBefore.sql"));
		string Verify = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts/VerifyBookmarkListDescription.sql"));
		Assert.Equal(1L, ReadRows(Legacy, Precheck)[0][0]);
		Assert.Equal(0L, ReadRows(Legacy, Verify)[0][0]);
		ExecuteSql(Legacy, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts/AddBookmarkListDescription.sql")));
		Assert.Equal(1L, ReadRows(Legacy, Verify)[0][0]);
		Assert.Equal(0L, ReadRows(Legacy, Precheck)[0][0]);
		Assert.Equal(ListsBefore, ReadRows(Legacy, ListsQuery));
		Assert.Equal(EntriesBefore, ReadRows(Legacy, EntriesQuery));
		Assert.Equal(IndexesBefore, ReadRows(Legacy, IndexesQuery));
		Assert.Equal(0L, ReadRows(Legacy, "SELECT COUNT(*) FROM BookmarkLists WHERE Description IS NOT NULL")[0][0]);
		using SqliteConnection Fresh = new("Data Source=:memory:;Foreign Keys=True");
		Fresh.Open();
		ExecuteSql(Fresh, FreshSql);
		Assert.Equal(ReadRows(Fresh, "PRAGMA table_info('BookmarkLists')"), ReadRows(Legacy, "PRAGMA table_info('BookmarkLists')"));
		Assert.Equal(1L, ReadRows(Fresh, Verify)[0][0]);
	}

	/// <summary>Executes a complete shipped SQL script against a disposable provider connection.</summary>
	/// <param name="Connection">Open test database.</param>
	/// <param name="Sql">Trusted fixture or repository SQL.</param>
	private static void ExecuteSql(SqliteConnection Connection, string Sql)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = Sql;
		Command.ExecuteNonQuery();
	}

	/// <summary>Captures every projected provider value for data-preservation and schema comparisons.</summary>
	/// <param name="Connection">Open test database.</param>
	/// <param name="Sql">Trusted read-only fixture query.</param>
	/// <returns>Rows in the query's explicit order, with SQL nulls represented by <see cref="DBNull.Value"/>.</returns>
	private static IReadOnlyList<object[]> ReadRows(SqliteConnection Connection, string Sql)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = Sql;
		using SqliteDataReader Reader = Command.ExecuteReader();
		List<object[]> Rows = [];
		while (Reader.Read())
		{
			object[] Values = new object[Reader.FieldCount];
			Reader.GetValues(Values);
			Rows.Add(Values);
		}
		return Rows;
	}

	/// <summary>Creates an independent scoped context with the same SQLite transaction retry strategy as the application.</summary>
	/// <param name="SiteId">Site enforced by named query filters.</param>
	/// <param name="Culture">Current article language; list descriptions are shared across languages.</param>
	/// <returns>A context for the caller to dispose.</returns>
	private WikiWikiWorldDbContext CreateContext(int SiteId = 1, string Culture = "en")
	{
		DbContextOptions<WikiWikiWorldDbContext> Options = new DbContextOptionsBuilder<WikiWikiWorldDbContext>()
			.UseSqlite(Connection, Options => Options.ExecutionStrategy(Dependencies => new SqliteRetryExecutionStrategy(Dependencies))).Options;
		return new(Options, new FixedSiteContext(SiteId, Culture));
	}

	/// <summary>Supplies a real site/culture scope for provider permission checks.</summary>
	/// <param name="SiteId">Current site.</param>
	/// <param name="Culture">Current article language.</param>
	private sealed class FixedSiteContext(int SiteId, string Culture) : ISiteContextService
	{
		/// <inheritdoc/>
		public int? GetCurrentSiteId() => SiteId;
		/// <inheritdoc/>
		public string? GetCurrentCulture() => Culture;
	}
}
