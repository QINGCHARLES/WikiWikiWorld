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
	/// </summary>
	/// <param name="Context">The database context.</param>
	private static void ApplyDurabilityPragma(DbContext? Context)
	{
		if (Context?.Database.GetDbConnection() is SqliteConnection Connection &&
			Connection.State == ConnectionState.Open)
		{
			string Synchronous = WriteDurabilityScope.Current is WriteDurability.High ? "FULL" : "NORMAL";
			using SqliteCommand Command = Connection.CreateCommand();
			Command.CommandText = $"PRAGMA synchronous = {Synchronous};";
			Command.ExecuteNonQuery();
		}
	}

	/// <summary>
	/// Asynchronously applies the synchronous PRAGMA based on the current durability scope.
	/// </summary>
	/// <param name="Context">The database context.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	private static async Task ApplyDurabilityPragmaAsync(DbContext? Context, CancellationToken CancellationToken)
	{
		if (Context?.Database.GetDbConnection() is SqliteConnection Connection &&
			Connection.State == ConnectionState.Open)
		{
			string Synchronous = WriteDurabilityScope.Current is WriteDurability.High ? "FULL" : "NORMAL";
			await using SqliteCommand Command = Connection.CreateCommand();
			Command.CommandText = $"PRAGMA synchronous = {Synchronous};";
			await Command.ExecuteNonQueryAsync(CancellationToken).ConfigureAwait(false);
		}
	}
}
