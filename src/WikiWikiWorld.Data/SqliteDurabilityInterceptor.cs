namespace WikiWikiWorld.Data;

using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Interceptor that applies the appropriate synchronous PRAGMA before SaveChanges
/// based on the ambient WriteDurabilityScope.
/// </summary>
public sealed class SqliteDurabilityInterceptor : SaveChangesInterceptor
{
	/// <inheritdoc/>
	public override InterceptionResult<int> SavingChanges(
		DbContextEventData EventData,
		InterceptionResult<int> Result)
	{
		ApplyDurabilityPragma(EventData.Context);
		return base.SavingChanges(EventData, Result);
	}

	/// <inheritdoc/>
	public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData EventData,
		InterceptionResult<int> Result,
		CancellationToken CancellationToken = default)
	{
		await ApplyDurabilityPragmaAsync(EventData.Context, CancellationToken).ConfigureAwait(false);
		return await base.SavingChangesAsync(EventData, Result, CancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Applies the synchronous PRAGMA based on the current durability scope.
	/// Silently skips if inside a transaction since SQLite forbids changing this pragma mid-transaction.
	/// </summary>
	/// <param name="Context">The database context.</param>
	private static void ApplyDurabilityPragma(DbContext? Context)
	{
		if (Context?.Database.GetDbConnection() is not SqliteConnection Connection ||
			Connection.State != ConnectionState.Open)
		{
			return;
		}

		string Synchronous = WriteDurabilityScope.Current is WriteDurability.High ? "FULL" : "NORMAL";
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = $"PRAGMA synchronous = {Synchronous};";

		try
		{
			Command.ExecuteNonQuery();
		}
		catch (SqliteException Ex) when (Ex.SqliteErrorCode == 1)
		{
			// SQLite forbids changing PRAGMA synchronous inside a transaction.
			// This can occur with raw "BEGIN IMMEDIATE" transactions that bypass
			// EF Core's CurrentTransaction tracking. Safe to skip — the pragma
			// is a session-level setting that persists from before the transaction.
		}
	}

	/// <summary>
	/// Asynchronously applies the synchronous PRAGMA based on the current durability scope.
	/// Silently skips if inside a transaction since SQLite forbids changing this pragma mid-transaction.
	/// </summary>
	/// <param name="Context">The database context.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	private static async Task ApplyDurabilityPragmaAsync(DbContext? Context, CancellationToken CancellationToken)
	{
		if (Context?.Database.GetDbConnection() is not SqliteConnection Connection ||
			Connection.State != ConnectionState.Open)
		{
			return;
		}

		string Synchronous = WriteDurabilityScope.Current is WriteDurability.High ? "FULL" : "NORMAL";
		await using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = $"PRAGMA synchronous = {Synchronous};";

		try
		{
			await Command.ExecuteNonQueryAsync(CancellationToken).ConfigureAwait(false);
		}
		catch (SqliteException Ex) when (Ex.SqliteErrorCode == 1)
		{
			// SQLite forbids changing PRAGMA synchronous inside a transaction.
			// This can occur with raw "BEGIN IMMEDIATE" transactions that bypass
			// EF Core's CurrentTransaction tracking. Safe to skip — the pragma
			// is a session-level setting that persists from before the transaction.
		}
	}
}
