using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Verifies exact upload claims on EF, fresh SQL, and upgraded existing SQLite schemas.</summary>
public sealed class FileUploadSchemaTests
{
	/// <summary>Preserves old rows and enforces one revision per non-null claim through all schema creation routes.</summary>
	/// <param name="SchemaSource">The EF, fresh SQL, or manual upgrade route.</param>
	[Theory]
	[InlineData("Ef")]
	[InlineData("Create")]
	[InlineData("Upgrade")]
	public async Task StagingClaimsPreserveExistingRowsAndRejectDuplicates(string SchemaSource)
	{
		using SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
		Connection.Open();
		DbContextOptions<WikiWikiWorldDbContext> Options = new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options;
		using WikiWikiWorldDbContext Context = new(Options);
		if (SchemaSource == "Create")
		{
			Execute(Connection, ReadScript("Create.sql"));
		}
		else
		{
			Context.Database.EnsureCreated();
		}
		Guid UserId = Guid.NewGuid();
		Context.Users.Add(new() { Id = UserId, UserName = "Uploader" });
		await Context.SaveChangesAsync();
		FileRevision Existing = new()
		{
			CanonicalFileId = Guid.NewGuid(), Filename = "old.svg", MimeType = "image/svg+xml", FileSizeBytes = 123,
			Type = FileType.Image2D, IsCurrent = true, CreatedByUserId = UserId, DateCreated = DateTimeOffset.UtcNow,
			RevisionReason = "Original upload", SourceAndRevisionReasonCulture = "en"
		};
		Context.FileRevisions.Add(Existing);
		Context.FileRevisions.Add(Existing with { CanonicalFileId = Guid.NewGuid(), IsCurrent = false });
		await Context.SaveChangesAsync();
		Context.ChangeTracker.Clear();
		string Before = JsonSerializer.Serialize(await Context.FileRevisions.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync());

		if (SchemaSource == "Upgrade")
		{
			// Reconstruct the pre-upgrade shape around representative populated rows, then execute the shipped SQL.
			Execute(Connection, "DROP INDEX UX_FileRevisions_StagingUploadId; ALTER TABLE FileRevisions DROP COLUMN StagingUploadId;");
			Assert.Equal(1L, Scalar(Connection, ReadScript("CheckFileRevisionStagingUploadIdBefore.sql")));
			Execute(Connection, ReadScript("AddFileRevisionStagingUploadId.sql"));
			Assert.Equal(0L, Scalar(Connection, ReadScript("CheckFileRevisionStagingUploadIdBefore.sql")));
		}
		Assert.Equal(1L, Scalar(Connection, ReadScript("VerifyFileRevisionStagingUploadId.sql")));
		Assert.Equal(Before, JsonSerializer.Serialize(await Context.FileRevisions.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync()));
		Assert.All(await Context.FileRevisions.ToArrayAsync(), r => Assert.Null(r.StagingUploadId));

		Guid Claim = Guid.NewGuid();
		FileRevision Claimed = Existing with { Id = 0, StagingUploadId = Claim };
		Context.FileRevisions.Add(Claimed);
		await Context.SaveChangesAsync();
		Context.ChangeTracker.Clear();
		Assert.Equal(Claim, (await Context.FileRevisions.AsNoTracking().SingleAsync(r => r.Id == Claimed.Id)).StagingUploadId);
		Context.FileRevisions.Add(Existing with { Id = 0, StagingUploadId = Claim, DateDeleted = DateTimeOffset.UtcNow });
		DbUpdateException Failure = await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
		Assert.IsType<SqliteException>(Failure.InnerException);
	}

	/// <summary>Loads the exact schema or assertion distributed with the application source.</summary>
	/// <param name="Name">The script filename copied into the test output.</param>
	/// <returns>The script text, without rewriting its statements for the test.</returns>
	private static string ReadScript(string Name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", Name));

	/// <summary>Applies fixed schema statements to a disposable database.</summary>
	/// <param name="Connection">The test's open SQLite connection.</param>
	/// <param name="Sql">The trusted schema or fixture statements.</param>
	private static void Execute(SqliteConnection Connection, string Sql)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = Sql;
		Command.ExecuteNonQuery();
	}

	/// <summary>Checks the one-cell integer assertion contract expected by the manual maintenance runner.</summary>
	/// <param name="Connection">The test's open SQLite connection.</param>
	/// <param name="Sql">The shipped precheck or verification query.</param>
	/// <returns>The assertion's integer result.</returns>
	private static long Scalar(SqliteConnection Connection, string Sql)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = Sql;
		using SqliteDataReader Reader = Command.ExecuteReader();
		Assert.Equal(1, Reader.FieldCount);
		Assert.True(Reader.Read());
		long Result = Reader.GetInt64(0);
		Assert.False(Reader.Read());
		return Result;
	}
}
