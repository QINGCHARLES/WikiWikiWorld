using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Exercises membership constraints and EF access against SQLite schemas created by each supported route.
/// </summary>
public sealed class ArticleCategoryMembershipSchemaTests
{
	/// <summary>
	/// Verifies SQL and EF schemas reject duplicates and orphans, preserve revisions, and index category lookups.
	/// </summary>
	/// <param name="SchemaSource">The EF, fresh SQL, or existing-database upgrade route to exercise.</param>
	[Theory]
	[InlineData("Ef")]
	[InlineData("Create")]
	[InlineData("Upgrade")]
	public void MembershipConstraintsAndLookupWork(string SchemaSource)
	{
		using SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
		Connection.Open();
		DbContextOptions<WikiWikiWorldDbContext> Options = new DbContextOptionsBuilder<WikiWikiWorldDbContext>()
			.UseSqlite(Connection).Options;
		using WikiWikiWorldDbContext Context = new(Options);
		if (SchemaSource == "Create")
		{
			Execute(Connection, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", "Create.sql")));
		}
		else
		{
			Context.Database.EnsureCreated();
			if (SchemaSource == "Upgrade")
				Execute(Connection, "DROP TABLE ArticleCategoryMemberships;");
		}

		Execute(Connection, """
			INSERT INTO Users (Id, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, DateCreated)
			VALUES ('author', 0, 0, 0, 0, 0, '2026-10-21T00:00:00Z');
			INSERT INTO ArticleRevisions (Id, CanonicalArticleId, SiteId, Culture, Title, UrlSlug, IsCurrent, Type, Text, RevisionReason, CreatedByUserId, DateCreated)
			VALUES (1, 'article', 1, 'en', 'Original title', 'example', 1, 0, 'Original text', 'Created', 'author', '2026-10-21T00:00:00Z');
			""");

		if (SchemaSource == "Upgrade")
			Execute(Connection, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", "AddArticleCategoryMemberships.sql")));

		Context.ArticleCategoryMemberships.Add(new() { ArticleRevisionId = 1, CategorySlug = "fashion-magazines" });
		Context.SaveChanges();
		Assert.Equal("fashion-magazines", Context.ArticleCategoryMemberships.AsNoTracking().Single().CategorySlug);
		AssertConstraint(Connection, "INSERT INTO ArticleCategoryMemberships VALUES (1, 'fashion-magazines');");
		AssertConstraint(Connection, "INSERT INTO ArticleCategoryMemberships VALUES (999, 'fashion-magazines');");
		AssertConstraint(Connection, "INSERT INTO ArticleCategoryMemberships VALUES (1, NULL);");
		Execute(Connection, "INSERT INTO ArticleCategoryMemberships VALUES (1, 'publications-in-english');");

		using SqliteCommand PlanCommand = Connection.CreateCommand();
		PlanCommand.CommandText = "EXPLAIN QUERY PLAN SELECT ArticleRevisionId FROM ArticleCategoryMemberships WHERE CategorySlug = 'fashion-magazines';";
		using (SqliteDataReader Reader = PlanCommand.ExecuteReader())
		{
			Assert.True(Reader.Read());
			Assert.Contains("IX_ArticleCategoryMemberships_CategorySlug_ArticleRevisionId", Reader.GetString(3));
		}

		using SqliteCommand SourceCommand = Connection.CreateCommand();
		SourceCommand.CommandText = "SELECT Text FROM ArticleRevisions WHERE Id = 1;";
		Assert.Equal("Original text", SourceCommand.ExecuteScalar());
		Execute(Connection, "UPDATE ArticleRevisions SET IsCurrent = 0, DateDeleted = '2026-10-22T00:00:00Z' WHERE Id = 1;");
		Assert.Equal(2, Context.ArticleCategoryMemberships.Count());
		Execute(Connection, "DELETE FROM ArticleRevisions WHERE Id = 1;");
		Assert.Empty(Context.ArticleCategoryMemberships.AsNoTracking());
	}

	/// <summary>
	/// Executes fixed schema or fixture SQL against the disposable test connection.
	/// </summary>
	/// <param name="Connection">The open in-memory database connection.</param>
	/// <param name="Sql">The schema or fixture statement to execute.</param>
	private static void Execute(SqliteConnection Connection, string Sql)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = Sql;
		Command.ExecuteNonQuery();
	}

	/// <summary>
	/// Confirms SQLite rejects a statement for a constraint violation.
	/// </summary>
	/// <param name="Connection">The open in-memory database connection.</param>
	/// <param name="Sql">The deliberately invalid fixture statement.</param>
	private static void AssertConstraint(SqliteConnection Connection, string Sql)
	{
		SqliteException Exception = Assert.Throws<SqliteException>(() => Execute(Connection, Sql));
		Assert.Equal(19, Exception.SqliteErrorCode);
	}
}
