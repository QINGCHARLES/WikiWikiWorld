namespace WikiWikiWorld.Data;

using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Applies PRAGMA synchronous based on the ambient <see cref="WriteDurabilityScope"/>.
/// The default (no scope) is High (FULL); ephemeral hot paths opt down via
/// <see cref="WriteDurabilityScope.Normal"/>. See doc/write-durability.md.
/// </summary>
/// <remarks>
/// The pragma cannot be changed while a transaction is active, so it is applied at
/// the three points where no transaction can exist yet:
/// <list type="bullet">
/// <item>connection open — deterministic baseline (PRAGMA synchronous is per-connection
/// and pooled connections otherwise keep whatever the previous user set), and covers
/// single-statement saves where EF Core skips the implicit transaction;</item>
/// <item>transaction starting — fires before BEGIN is issued, so explicit transactions
/// commit at the level of the scope that was ambient when the transaction started;</item>
/// <item>saving changes — only for non-transactional saves on a connection that was
/// opened earlier and kept open (possibly under a different ambient scope).</item>
/// </list>
/// Failures are deliberately NOT swallowed: with this ordering the pragma can only be
/// rejected if a transaction was started outside EF Core's tracking, which is a bug
/// that must surface rather than silently downgrade durability.
/// </remarks>
public sealed class SqliteDurabilityInterceptor : IDbConnectionInterceptor, IDbTransactionInterceptor, ISaveChangesInterceptor
{
	/// <inheritdoc/>
	public void ConnectionOpened(DbConnection Connection, ConnectionEndEventData EventData)
	{
		if (Connection is SqliteConnection SqliteConnection)
		{
			ApplyDurabilityPragma(SqliteConnection);
		}
	}

	/// <inheritdoc/>
	public async Task ConnectionOpenedAsync(DbConnection Connection, ConnectionEndEventData EventData, CancellationToken CancellationToken = default)
	{
		if (Connection is SqliteConnection SqliteConnection)
		{
			await ApplyDurabilityPragmaAsync(SqliteConnection, CancellationToken).ConfigureAwait(false);
		}
	}

	/// <inheritdoc/>
	public InterceptionResult<DbTransaction> TransactionStarting(DbConnection Connection, TransactionStartingEventData EventData, InterceptionResult<DbTransaction> Result)
	{
		if (Connection is SqliteConnection SqliteConnection)
		{
			ApplyDurabilityPragma(SqliteConnection);
		}

		return Result;
	}

	/// <inheritdoc/>
	public async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection Connection, TransactionStartingEventData EventData, InterceptionResult<DbTransaction> Result, CancellationToken CancellationToken = default)
	{
		if (Connection is SqliteConnection SqliteConnection)
		{
			await ApplyDurabilityPragmaAsync(SqliteConnection, CancellationToken).ConfigureAwait(false);
		}

		return Result;
	}

	/// <inheritdoc/>
	public InterceptionResult<int> SavingChanges(DbContextEventData EventData, InterceptionResult<int> Result)
	{
		if (TryGetOpenNonTransactionalConnection(EventData.Context) is SqliteConnection SqliteConnection)
		{
			ApplyDurabilityPragma(SqliteConnection);
		}

		return Result;
	}

	/// <inheritdoc/>
	public async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData EventData, InterceptionResult<int> Result, CancellationToken CancellationToken = default)
	{
		if (TryGetOpenNonTransactionalConnection(EventData.Context) is SqliteConnection SqliteConnection)
		{
			await ApplyDurabilityPragmaAsync(SqliteConnection, CancellationToken).ConfigureAwait(false);
		}

		return Result;
	}

	/// <summary>
	/// Returns the context's connection when it is open and no EF-tracked transaction is
	/// active. Inside an explicit transaction the pragma was already applied at
	/// TransactionStarting and cannot (and must not) be changed mid-transaction.
	/// </summary>
	/// <param name="Context">The database context.</param>
	/// <returns>The open, transaction-free connection, or null.</returns>
	private static SqliteConnection? TryGetOpenNonTransactionalConnection(DbContext? Context)
	{
		if (Context is null || Context.Database.CurrentTransaction is not null)
		{
			return null;
		}

		return Context.Database.GetDbConnection() is SqliteConnection { State: ConnectionState.Open } Connection
			? Connection
			: null;
	}

	private static string BuildPragmaSql() =>
		$"PRAGMA synchronous = {(WriteDurabilityScope.Current is WriteDurability.Normal ? "NORMAL" : "FULL")};";

	private static void ApplyDurabilityPragma(SqliteConnection Connection)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = BuildPragmaSql();
		Command.ExecuteNonQuery();
	}

	private static async Task ApplyDurabilityPragmaAsync(SqliteConnection Connection, CancellationToken CancellationToken)
	{
		await using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = BuildPragmaSql();
		await Command.ExecuteNonQueryAsync(CancellationToken).ConfigureAwait(false);
	}
}
