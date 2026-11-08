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

/// <summary>Checks per-membership Markdown notes and their additive schema upgrade against actual SQLite.</summary>
public sealed partial class BookmarkManagementTests
{
	/// <summary>The same article has independent notes in different lists without changing save times or list introductions.</summary>
	[Fact]
	public async Task NotesBelongToMembershipsAndPreserveSaveMetadata()
	{
		int DefaultId = await SeedAsync(1);
		await using WikiWikiWorldDbContext Context = CreateContext(1);
		BookmarkService Service = new(Context);
		Bookmark Original = await Context.Bookmarks.AsNoTracking().SingleAsync();
		int CustomId = (await Service.CreateEmptyListAsync("Research", false, Owner, 1, "*List introduction*", default)).ListId!.Value;
		Assert.True(await Service.SetBookmarkInListAsync(CustomId, Original.CanonicalArticleId, Owner, 1, true, default));
		int CustomEntryId = await Context.Bookmarks.Where(b => b.BookmarkListId == CustomId).Select(b => b.Id).SingleAsync();
		const string Source = "  *Research* [Archive](https://example.com)  \n日本語\n";
		Assert.True(await Service.SetBookmarkNotesAsync(Original.Id, Source, Owner, default));
		Assert.True(await Service.SetBookmarkNotesAsync(CustomEntryId, "Another reason", Owner, default));
		Assert.True(await Service.SetBookmarkInListAsync(DefaultId, Original.CanonicalArticleId, Owner, 1, true, default));
		Assert.Equal(BookmarkListCreateResult.AddedToExisting, await Service.CreateListWithBookmarkAsync("research", Original.CanonicalArticleId, Owner, 1, default));
		BookmarkEntry First = Assert.Single((await Service.GetListPageAsync(DefaultId, Owner, true, "recent", 1, default)).Items);
		BookmarkEntry Second = Assert.Single((await Service.GetListPageAsync(CustomId, Owner, true, "name", 1, default)).Items);
		Assert.Equal(Source, First.Notes);
		Assert.Equal("Another reason", Second.Notes);
		Assert.Equal(Original.DateCreated, First.DateCreated);
		Assert.Equal(Original with { Notes = Source }, await Context.Bookmarks.AsNoTracking().SingleAsync(b => b.Id == Original.Id));
		Assert.Equal("*List introduction*", (await Service.GetListDetailsAsync(CustomId, Owner, true, default))!.Description);
		Assert.Equal(Bookmark.MaxNotesLength, Context.Model.FindEntityType(typeof(Bookmark))!.FindProperty(nameof(Bookmark.Notes))!.GetMaxLength());
	}

	/// <summary>Every supported blank input clears a previously stored note rather than storing empty Markdown.</summary>
	/// <param name="Blank">One representation of an absent note.</param>
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" \t\r\n")]
	public async Task BlankBookmarkNotesBecomeNull(string? Blank)
	{
		await SeedAsync(1);
		await using WikiWikiWorldDbContext Context = CreateContext(1);
		BookmarkService Service = new(Context);
		int EntryId = await Context.Bookmarks.Select(b => b.Id).SingleAsync();
		Assert.True(await Service.SetBookmarkNotesAsync(EntryId, "Old reason", Owner, default));
		Assert.True(await Service.SetBookmarkNotesAsync(EntryId, Blank, Owner, default));
		Assert.Null((await Context.Bookmarks.AsNoTracking().SingleAsync()).Notes);
	}

	/// <summary>The exact source-length limit succeeds, oversized edits retain prior data, and unknown IDs never create state.</summary>
	[Fact]
	public async Task BookmarkNotesValidateLengthBeforeWriting()
	{
		await SeedAsync(1);
		await using WikiWikiWorldDbContext Context = CreateContext(1);
		BookmarkService Service = new(Context);
		int EntryId = await Context.Bookmarks.Select(b => b.Id).SingleAsync();
		string AtLimit = new('x', Bookmark.MaxNotesLength);
		Assert.True(await Service.SetBookmarkNotesAsync(EntryId, AtLimit, Owner, default));
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service.SetBookmarkNotesAsync(EntryId, AtLimit + "x", Owner, default));
		Assert.Equal(AtLimit, (await Context.Bookmarks.AsNoTracking().SingleAsync()).Notes);
		Assert.False(await Service.SetBookmarkNotesAsync(int.MaxValue, "Missing", Owner, default));
		Assert.Single(await Context.Bookmarks.ToListAsync());
	}

	/// <summary>Both the membership and parent must be active, owned and in scope before an edit is accepted.</summary>
	[Fact]
	public async Task BookmarkNoteWritesRejectForeignOwnersSitesAndDeletedParents()
	{
		int ListId = await SeedAsync(1);
		int EntryId;
		await using (WikiWikiWorldDbContext Context = CreateContext(1))
		{
			BookmarkService Service = new(Context);
			EntryId = await Context.Bookmarks.Select(b => b.Id).SingleAsync();
			Assert.False(await Service.SetBookmarkNotesAsync(EntryId, "Foreign owner", Other, default));
			Assert.True(await Service.SetBookmarkNotesAsync(EntryId, "Retained", Owner, default));
		}
		await using (WikiWikiWorldDbContext Foreign = CreateContext(2))
			Assert.False(await new BookmarkService(Foreign).SetBookmarkNotesAsync(EntryId, "Foreign site", Owner, default));
		await using (WikiWikiWorldDbContext Context = CreateContext(1))
		{
			await Context.BookmarkLists.Where(l => l.Id == ListId).ExecuteUpdateAsync(s => s.SetProperty(l => l.DateDeleted, DateTimeOffset.UtcNow));
			Assert.False(await new BookmarkService(Context).SetBookmarkNotesAsync(EntryId, "Deleted parent", Owner, default));
			Assert.Equal("Retained", (await Context.Bookmarks.AsNoTracking().SingleAsync()).Notes);
		}
	}

	/// <summary>An inconsistent ownership relationship cannot let a membership's creator edit another owner's list entry.</summary>
	[Fact]
	public async Task BookmarkNoteWritesRequireParentOwnership()
	{
		int ListId = await SeedAsync(1);
		await using WikiWikiWorldDbContext Context = CreateContext(1);
		int EntryId = await Context.Bookmarks.Select(b => b.Id).SingleAsync();
		await Context.BookmarkLists.Where(l => l.Id == ListId).ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedByUserId, Other));
		Assert.False(await new BookmarkService(Context).SetBookmarkNotesAsync(EntryId, "Not this owner's list", Owner, default));
		Assert.Null((await Context.Bookmarks.AsNoTracking().SingleAsync()).Notes);
	}

	/// <summary>Only visible list pages expose notes, and authorized source uses the shared short-content Markdown policy.</summary>
	[Fact]
	public async Task VisibleBookmarkNotesUseSharedMarkdownRendering()
	{
		int ListId = await SeedAsync(1);
		await using WikiWikiWorldDbContext Context = CreateContext(1);
		BookmarkService Service = new(Context);
		int EntryId = await Context.Bookmarks.Select(b => b.Id).SingleAsync();
		const string Source = "*Research* **Important** [Archive](https://example.com)\n\n<script>alert(1)</script>\n\n[Unsafe](javascript:alert(1))\n\n{{Stub}}";
		Assert.True(await Service.SetBookmarkNotesAsync(EntryId, Source, Owner, default));
		Assert.Empty((await Service.GetListPageAsync(ListId, Owner, false, "recent", 1, default)).Items);
		Assert.True(await Service.SetListVisibilityAsync(ListId, true, Owner, 1, default));
		BookmarkEntry Entry = Assert.Single((await Service.GetListPageAsync(ListId, Owner, false, "recent", 1, default)).Items);
		Assert.Equal(Source, Entry.Notes);
		MarkdownPipelineFactory Factory = new(Options.Create(new SiteConfiguration()));
		using IDocument Document = new HtmlParser().ParseDocument(Factory.RenderUserContentMarkdown(Entry.Notes));
		Assert.Equal("Research", Document.QuerySelector("em")!.TextContent);
		Assert.Equal("Important", Document.QuerySelector("strong")!.TextContent);
		Assert.Equal("https://example.com", Document.QuerySelector("a")!.GetAttribute("href"));
		Assert.Equal(string.Empty, Document.QuerySelectorAll("a")[1].GetAttribute("href"));
		Assert.Null(Document.QuerySelector("script"));
		Assert.Contains("{{Stub}}", Document.Body!.TextContent, StringComparison.Ordinal);
		Assert.True(await Service.SetListVisibilityAsync(ListId, false, Owner, 1, default));
		Assert.Empty((await Service.GetListPageAsync(ListId, Owner, false, "recent", 1, default)).Items);
	}

	/// <summary>Removal retains the original note and save date for undo; re-adding carries the latest removed note forward, and old undo cannot overwrite a later edit.</summary>
	[Fact]
	public async Task BookmarkNotesSurviveUndoAndCarryIntoReaddedEntries()
	{
		int ListId = await SeedAsync(1);
		Bookmark Original;
		DateTimeOffset RemovedAt;
		await using (WikiWikiWorldDbContext Context = CreateContext(1))
		{
			BookmarkService Service = new(Context);
			Original = await Context.Bookmarks.AsNoTracking().SingleAsync();
			Assert.True(await Service.SetBookmarkNotesAsync(Original.Id, "Original reason", Owner, default));
			Assert.True(await Service.SetBookmarkInListAsync(ListId, Original.CanonicalArticleId, Owner, 1, false, default));
			Bookmark Removed = await Context.Bookmarks.IgnoreQueryFilters([WikiWikiWorldDbContext.SoftDeleteFilter]).AsNoTracking().SingleAsync();
			RemovedAt = Removed.DateDeleted!.Value;
			Assert.Equal("Original reason", Removed.Notes);
			Assert.False(await Service.SetBookmarkNotesAsync(Original.Id, "Deleted entry", Owner, default));
		}
		await using (WikiWikiWorldDbContext Context = CreateContext(1))
		{
			BookmarkService Service = new(Context);
			Assert.True(await Service.RestoreEntryAsync(ListId, Original.Id, RemovedAt, Owner, default));
			BookmarkEntry Restored = Assert.Single((await Service.GetListPageAsync(ListId, Owner, true, "recent", 1, default)).Items);
			Assert.Equal("Original reason", Restored.Notes);
			Assert.Equal(Original.DateCreated, Restored.DateCreated);
			RemovedAt = (await Service.RemoveEntryAsync(ListId, Original.Id, Owner, default))!.Value;
		}
		int NewId;
		await using (WikiWikiWorldDbContext Context = CreateContext(1))
		{
			BookmarkService Service = new(Context);
			Assert.True(await Service.SetBookmarkInListAsync(ListId, Original.CanonicalArticleId, Owner, 1, true, default));
			BookmarkEntry Readded = Assert.Single((await Service.GetListPageAsync(ListId, Owner, true, "recent", 1, default)).Items);
			NewId = Readded.Id;
			Assert.NotEqual(Original.Id, NewId);
			Assert.Equal("Original reason", Readded.Notes);
			Assert.True(await Service.SetBookmarkNotesAsync(NewId, "New reason", Owner, default));
		}
		await using (WikiWikiWorldDbContext Context = CreateContext(1))
		{
			BookmarkService Service = new(Context);
			Assert.True(await Service.RestoreEntryAsync(ListId, Original.Id, RemovedAt, Owner, default));
			BookmarkEntry Current = Assert.Single((await Service.GetListPageAsync(ListId, Owner, true, "recent", 1, default)).Items);
			Assert.Equal(NewId, Current.Id);
			Assert.Equal("New reason", Current.Notes);
			Assert.True(await Service.DeleteOrClearListAsync(ListId, Owner, default));
			Assert.Equal("New reason", await Context.Bookmarks.IgnoreQueryFilters([WikiWikiWorldDbContext.SoftDeleteFilter])
				.Where(b => b.Id == NewId).Select(b => b.Notes).SingleAsync());
		}
	}

	/// <summary>Cancelled edits propagate cancellation and leave the stored note unchanged.</summary>
	[Fact]
	public async Task CancelledBookmarkNoteEditDoesNotWrite()
	{
		await SeedAsync(1);
		await using WikiWikiWorldDbContext Context = CreateContext(1);
		int EntryId = await Context.Bookmarks.Select(b => b.Id).SingleAsync();
		using CancellationTokenSource Cancellation = new();
		Cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BookmarkService(Context).SetBookmarkNotesAsync(EntryId, "Cancelled", Owner, Cancellation.Token));
		Assert.Null((await Context.Bookmarks.AsNoTracking().SingleAsync()).Notes);
	}

	/// <summary>The shipped upgrade preserves list descriptions, memberships, indexes and deleted identity history and matches fresh columns.</summary>
	[Fact]
	public void BookmarkNotesUpgradePreservesExistingDataAndSchema()
	{
		string FreshSql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts/Create.sql")).Replace("\r\n", "\n", StringComparison.Ordinal);
		const string NotesColumn = "    Notes TEXT NULL,\n";
		Assert.Contains(NotesColumn, FreshSql, StringComparison.Ordinal);
		using SqliteConnection Legacy = new("Data Source=:memory:;Foreign Keys=True");
		Legacy.Open();
		ExecuteNoteSql(Legacy, FreshSql.Replace(NotesColumn, "", StringComparison.Ordinal));
		ExecuteNoteSql(Legacy, """
			INSERT INTO Users (Id, EmailConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, DateCreated)
			VALUES ('owner', 0, 0, 0, 0, '2026-01-01');
			INSERT INTO BookmarkLists (Id, SiteId, Name, IsDefault, IsPublic, CreatedByUserId, DateCreated, Description)
			VALUES (1, 1, 'Reading café 日本語', 0, 1, 'owner', '2026-01-01', '*Existing header*');
			INSERT INTO Bookmarks (Id, BookmarkListId, SiteId, CanonicalArticleId, CreatedByUserId, DateCreated, DateDeleted)
			VALUES (1, 1, 1, 'article-1', 'owner', '2026-01-02', NULL),
			       (2, 1, 1, 'article-2', 'owner', '2026-01-03', '2026-01-04'),
			       (11, 1, 1, 'deleted-identity', 'owner', '2026-01-03', '2026-01-04');
			DELETE FROM Bookmarks WHERE Id = 11;
			""");
		const string EntriesQuery = "SELECT Id, BookmarkListId, SiteId, CanonicalArticleId, CreatedByUserId, DateCreated, DateDeleted FROM Bookmarks ORDER BY Id";
		const string ListsQuery = "SELECT Id, SiteId, Name, IsDefault, IsPublic, CreatedByUserId, DateCreated, DateModified, DateDeleted, Description FROM BookmarkLists ORDER BY Id";
		const string IndexesQuery = "SELECT name, sql FROM sqlite_schema WHERE type = 'index' ORDER BY name";
		const string SequencesQuery = "SELECT name, seq FROM sqlite_sequence ORDER BY name";
		IReadOnlyList<object[]> Entries = ReadNoteRows(Legacy, EntriesQuery);
		IReadOnlyList<object[]> Lists = ReadNoteRows(Legacy, ListsQuery);
		IReadOnlyList<object[]> Indexes = ReadNoteRows(Legacy, IndexesQuery);
		IReadOnlyList<object[]> Sequences = ReadNoteRows(Legacy, SequencesQuery);
		string Precheck = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts/CheckBookmarkNotesBefore.sql"));
		string Verify = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts/VerifyBookmarkNotes.sql"));
		Assert.Equal(1L, ReadNoteRows(Legacy, Precheck)[0][0]);
		Assert.Equal(0L, ReadNoteRows(Legacy, Verify)[0][0]);
		ExecuteNoteSql(Legacy, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts/AddBookmarkNotes.sql")));
		Assert.Equal(1L, ReadNoteRows(Legacy, Verify)[0][0]);
		Assert.Equal(0L, ReadNoteRows(Legacy, Precheck)[0][0]);
		Assert.Equal(Entries, ReadNoteRows(Legacy, EntriesQuery));
		Assert.Equal(Lists, ReadNoteRows(Legacy, ListsQuery));
		Assert.Equal(Indexes, ReadNoteRows(Legacy, IndexesQuery));
		Assert.Equal(Sequences, ReadNoteRows(Legacy, SequencesQuery));
		Assert.Equal(0L, ReadNoteRows(Legacy, "SELECT COUNT(*) FROM Bookmarks WHERE Notes IS NOT NULL")[0][0]);
		using SqliteConnection Fresh = new("Data Source=:memory:;Foreign Keys=True");
		Fresh.Open();
		ExecuteNoteSql(Fresh, FreshSql);
		Assert.Equal(ReadNoteRows(Fresh, "PRAGMA table_info('Bookmarks')"), ReadNoteRows(Legacy, "PRAGMA table_info('Bookmarks')"));
	}

	/// <summary>Runs a trusted repository script or fixture setup against an isolated provider database.</summary>
	/// <param name="Connection">Open disposable database.</param>
	/// <param name="Sql">Shipped SQL or constant test setup.</param>
	private static void ExecuteNoteSql(SqliteConnection Connection, string Sql)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = Sql;
		Command.ExecuteNonQuery();
	}

	/// <summary>Captures projected provider values for exact schema and retained-data comparisons.</summary>
	/// <param name="Connection">Open disposable database.</param>
	/// <param name="Sql">Trusted read-only test query with explicit ordering.</param>
	/// <returns>All rows, with SQL nulls represented by <see cref="DBNull.Value"/>.</returns>
	private static IReadOnlyList<object[]> ReadNoteRows(SqliteConnection Connection, string Sql)
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
}
