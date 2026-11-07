using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Verifies EF, fresh SQL and manual cutover schemas enforce private membership and preserve existing accounts.</summary>
public sealed class PrivateMessagingSchemaTests
{
	/// <summary>Every schema route supports member-authored messages and rejects nonmembers and duplicate memberships.</summary>
	/// <param name="Source">EF, fresh SQL, or the shipped upgrade.</param>
	/// <returns>Completion after SQLite constraints and schema parity are verified.</returns>
	[Theory]
	[InlineData("Ef")]
	[InlineData("Create")]
	[InlineData("Upgrade")]
	public async Task SchemaPathsEnforceAuthorMembership(string Source)
	{
		using SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
		Connection.Open();
		using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		if (Source == "Ef") Context.Database.EnsureCreated();
		else Execute(Connection, Script("Create.sql"));
		Guid Member = Guid.NewGuid();
		Guid Outsider = Guid.NewGuid();
		Context.Users.AddRange(new() { Id = Member, UserName = "Member" }, new() { Id = Outsider, UserName = "Outsider" });
		await Context.SaveChangesAsync();
		if (Source == "Upgrade")
		{
			RestoreOldShape(Connection);
			Assert.Equal(1L, Scalar(Connection, Script("CheckPrivateMessagingBefore.sql")));
			Execute(Connection, Script("ReplacePrivateMessaging.sql"));
			Assert.Equal(0L, Scalar(Connection, Script("CheckPrivateMessagingBefore.sql")));
		}
		if (Source != "Ef") Assert.Equal(1L, Scalar(Connection, Script("VerifyPrivateMessaging.sql")));
		Assert.Equal(2, await Context.Users.CountAsync());
		Assert.Equal(0L, Scalar(Connection, "SELECT count(*) FROM pragma_table_info('ArticleTalkSubjects') WHERE name IN ('HasBeenEdited','DateModified')"));
		Guid Id = Guid.NewGuid();
		PrivateConversation Conversation = new() { Id = Id, SiteId = 1, Culture = "en", Subject = "Title", CreatedByUserId = Member, DateCreated = DateTimeOffset.UtcNow };
		Conversation.Participants.Add(new() { UserId = Member });
		Conversation.Messages.Add(new() { AuthorUserId = Member, Text = "Body", DateCreated = DateTimeOffset.UtcNow });
		Context.PrivateConversations.Add(Conversation);
		await Context.SaveChangesAsync();
		Context.ChangeTracker.Clear();
		Context.PrivateMessages.Add(new() { ConversationId = Id, AuthorUserId = Outsider, Text = "Forbidden", DateCreated = DateTimeOffset.UtcNow });
		DbUpdateException Failure = await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
		Assert.Equal(19, Assert.IsType<SqliteException>(Failure.InnerException).SqliteErrorCode);
		Context.ChangeTracker.Clear();
		Context.PrivateConversationParticipants.Add(new() { ConversationId = Id, UserId = Member });
		await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
		Assert.Equal(1L, Scalar(Connection, "SELECT count(*) FROM PrivateMessages"));
		Assert.Equal(0L, Scalar(Connection, "SELECT count(*) FROM pragma_foreign_key_check"));
	}

	/// <summary>A changed inventory aborts inside the migration transaction even when an operator skips the precheck.</summary>
	[Fact]
	public void CutoverRefusesNewDiscussionRowsWithoutDeletingThem()
	{
		using SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
		Connection.Open();
		Execute(Connection, Script("Create.sql"));
		RestoreOldShape(Connection);
		using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		User Author = new() { Id = Guid.NewGuid(), UserName = "Author" };
		Context.Users.Add(Author);
		Context.SaveChanges();
		ArticleTalkSubject Subject = new() { SiteId = 1, CanonicalArticleId = Guid.NewGuid(), Subject = "Public discussion", UrlSlug = "public", CreatedByUserId = Author.Id, DateCreated = DateTimeOffset.UtcNow };
		Context.ArticleTalkSubjects.Add(Subject);
		Context.SaveChanges();
		Context.ArticleTalkSubjectPosts.Add(new() { ArticleTalkSubjectId = Subject.Id, Text = "Preserve me", CreatedByUserId = Author.Id, DateCreated = DateTimeOffset.UtcNow });
		Context.SaveChanges();
		Assert.Equal(0L, Scalar(Connection, Script("CheckPrivateMessagingBefore.sql")));
		Assert.Throws<SqliteException>(() => Execute(Connection, Script("ReplacePrivateMessaging.sql")));
		Execute(Connection, "ROLLBACK;");
		Assert.Equal(1, Context.ArticleTalkSubjects.Count());
		Assert.Equal("Preserve me", Context.ArticleTalkSubjectPosts.Single().Text);
		Assert.Equal(0L, Scalar(Connection, "SELECT count(*) FROM sqlite_schema WHERE name = 'PrivateConversations'"));
		Assert.Equal(2L, Scalar(Connection, "SELECT count(*) FROM pragma_table_info('ArticleTalkSubjects') WHERE name IN ('HasBeenEdited','DateModified')"));
	}

	/// <summary>The request-scoped EF filters hide all three private entity sets outside the resolved site/culture.</summary>
	/// <returns>Completion after parent and child filter behavior is verified.</returns>
	[Fact]
	public async Task ScopeFiltersCoverConversationsMembershipAndBodies()
	{
		using SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
		Connection.Open();
		DbContextOptions<WikiWikiWorldDbContext> Options = new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options;
		using (WikiWikiWorldDbContext Seed = new(Options))
		{
			Seed.Database.EnsureCreated();
			User Author = new() { Id = Guid.NewGuid(), UserName = "Member" };
			Seed.Users.Add(Author);
			PrivateConversation Conversation = new() { Id = Guid.NewGuid(), SiteId = 1, Culture = "en", Subject = "Scoped", CreatedByUserId = Author.Id, DateCreated = DateTimeOffset.UtcNow };
			Conversation.Participants.Add(new() { UserId = Author.Id });
			Conversation.Messages.Add(new() { AuthorUserId = Author.Id, Text = "Scoped body", DateCreated = DateTimeOffset.UtcNow });
			Seed.PrivateConversations.Add(Conversation);
			await Seed.SaveChangesAsync();
		}
		foreach ((int Site, string Culture, int Expected) in new[] { (1, "en", 1), (2, "en", 0), (1, "fr", 0) })
		{
			using WikiWikiWorldDbContext Scoped = new(Options, new Scope(Site, Culture));
			Assert.Equal(Expected, await Scoped.PrivateConversations.CountAsync());
			Assert.Equal(Expected, await Scoped.PrivateConversationParticipants.CountAsync());
			Assert.Equal(Expected, await Scoped.PrivateMessages.CountAsync());
		}
	}

	/// <summary>Reconstructs only the inventoried pre-cutover shape on disposable data, preserving other tables.</summary>
	/// <param name="Connection">Isolated SQLite database.</param>
	private static void RestoreOldShape(SqliteConnection Connection) => Execute(Connection,
		"DROP TABLE PrivateMessages; DROP TABLE PrivateConversationParticipants; DROP TABLE PrivateConversations; " +
		"ALTER TABLE ArticleTalkSubjects ADD COLUMN HasBeenEdited INTEGER NOT NULL DEFAULT 0; " +
		"ALTER TABLE ArticleTalkSubjects ADD COLUMN DateModified TEXT NULL;");

	/// <summary>Reads an exact shipped schema or assertion rather than reconstructing its SQL in the test.</summary>
	/// <param name="Name">Script filename.</param>
	/// <returns>The SQL copied into test output.</returns>
	private static string Script(string Name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", Name));

	/// <summary>Executes trusted fixture or shipped SQL on an isolated database.</summary>
	/// <param name="Connection">Open test connection.</param>
	/// <param name="Sql">Trusted statements.</param>
	private static void Execute(SqliteConnection Connection, string Sql)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = Sql;
		Command.ExecuteNonQuery();
	}

	/// <summary>Reads a one-cell integer schema assertion.</summary>
	/// <param name="Connection">Open test connection.</param>
	/// <param name="Sql">Read-only assertion.</param>
	/// <returns>The assertion result.</returns>
	private static long Scalar(SqliteConnection Connection, string Sql)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = Sql;
		return (long)Command.ExecuteScalar()!;
	}

	/// <summary>Supplies a real request filter scope without depending on HTTP.</summary>
	/// <param name="SiteId">Resolved site.</param>
	/// <param name="Culture">Resolved culture.</param>
	private sealed class Scope(int SiteId, string Culture) : ISiteContextService
	{
		/// <inheritdoc/>
		public int? GetCurrentSiteId() => SiteId;
		/// <inheritdoc/>
		public string? GetCurrentCulture() => Culture;
	}
}
