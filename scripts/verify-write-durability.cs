// Verifies that SqliteDurabilityInterceptor actually applies PRAGMA synchronous at the
// right moments — including INSIDE an explicit transaction, which was silently broken
// before (the pragma cannot change mid-transaction, so it must be set before BEGIN).
// See doc/write-durability.md.
//
// Run with: dotnet run scripts/verify-write-durability.cs

#:project ../src/WikiWikiWorld.Data/WikiWikiWorld.Data.csproj
#:property PublishAot=false

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WikiWikiWorld.Data;

const long Full = 2;
const long Normal = 1;

string DbPath = Path.Combine(Path.GetTempPath(), $"www-durability-check-{Guid.NewGuid():N}.db");
string ConnectionString = $"Data Source={DbPath}";
int Failures = 0;

try
{
	SqliteInitializer.Initialize(ConnectionString);

	DbContextOptions<WikiWikiWorldDbContext> Options = new DbContextOptionsBuilder<WikiWikiWorldDbContext>()
		.UseSqlite(ConnectionString)
		.AddInterceptors(new SqlitePragmaInterceptor(), new SqliteDurabilityInterceptor())
		.Options;

	// 1. No scope (ambient default) inside an explicit transaction => FULL.
	await using (WikiWikiWorldDbContext Context = new(Options))
	{
		await using IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync();
		Check("default (no scope), inside explicit transaction", await ReadSynchronousAsync(Context), Full);
		await Transaction.RollbackAsync();
	}

	// 2. WriteDurabilityScope.Normal() entered BEFORE the transaction => NORMAL.
	await using (WikiWikiWorldDbContext Context = new(Options))
	{
		using (WriteDurabilityScope.Normal())
		{
			await using IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync();
			Check("Normal scope, inside explicit transaction", await ReadSynchronousAsync(Context), Normal);
			await Transaction.RollbackAsync();
		}

		// 3. Scope disposed => next transaction on the SAME context is back to FULL.
		await using (IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync())
		{
			Check("after Normal scope disposed, next transaction", await ReadSynchronousAsync(Context), Full);
			await Transaction.RollbackAsync();
		}
	}

	// 4. Normal scope, connection opened with no transaction (single-statement save path).
	await using (WikiWikiWorldDbContext Context = new(Options))
	{
		using (WriteDurabilityScope.Normal())
		{
			await Context.Database.OpenConnectionAsync();
			Check("Normal scope, open connection, no transaction", await ReadSynchronousAsync(Context), Normal);
			await Context.Database.CloseConnectionAsync();
		}
	}
}
finally
{
	SqliteConnection.ClearAllPools();
	foreach (string StaleFile in Directory.GetFiles(Path.GetTempPath(), "www-durability-check-*"))
	{
		File.Delete(StaleFile);
	}
}

Console.WriteLine(Failures == 0 ? "\nPASS: all durability checks succeeded." : $"\nFAIL: {Failures} check(s) failed.");
return Failures == 0 ? 0 : 1;

void Check(string Name, long Actual, long Expected)
{
	bool Passed = Actual == Expected;
	if (!Passed)
	{
		Failures++;
	}

	Console.WriteLine($"{(Passed ? "ok  " : "FAIL")}  {Name}: synchronous = {Actual} (expected {Expected})");
}

static async Task<long> ReadSynchronousAsync(WikiWikiWorldDbContext Context)
{
	SqliteConnection Connection = (SqliteConnection)Context.Database.GetDbConnection();
	await using SqliteCommand Command = Connection.CreateCommand();
	Command.CommandText = "PRAGMA synchronous;";

	if (Context.Database.CurrentTransaction?.GetDbTransaction() is SqliteTransaction Transaction)
	{
		Command.Transaction = Transaction;
	}

	object? Value = await Command.ExecuteScalarAsync();
	return (long)Value!;
}
