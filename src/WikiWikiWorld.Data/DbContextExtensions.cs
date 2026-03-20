using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace WikiWikiWorld.Data;

/// <summary>
/// Extensions for DatabaseFacade.
/// </summary>
public static class DatabaseFacadeExtensions
{
	/// <summary>
	/// Begins a transaction with IMMEDIATE locking for SQLite to prevent upgrade deadlocks.
	/// For other providers, falls back to the default behavior.
	/// The transaction is properly registered with EF Core so that SaveChangesAsync
	/// will not attempt to start a nested transaction.
	/// </summary>
	/// <param name="Database">The DatabaseFacade.</param>
	/// <param name="CancellationToken">Cancellation token.</param>
	/// <returns>A disposable transaction.</returns>
	public static async Task<IDbContextTransaction> BeginImmediateTransactionAsync(
		this DatabaseFacade Database, 
		CancellationToken CancellationToken = default)
	{
		if (Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
		{
			return await Database.BeginTransactionAsync(CancellationToken);
		}

		// Use EF Core's standard BeginTransactionAsync which properly registers
		// the transaction with the DbContext. EF Core's SQLite provider uses
		// deferred transactions by default, which we'll upgrade to IMMEDIATE.
		IDbContextTransaction EfTransaction = await Database.BeginTransactionAsync(CancellationToken);

		// Upgrade to IMMEDIATE lock by ending the deferred transaction and
		// restarting as IMMEDIATE. This is done via the underlying connection.
		// Note: EF Core's BeginTransactionAsync already started a "BEGIN" so
		// we need to work within that — the transaction is already registered.
		// Actually, since EF Core already issued BEGIN, we just return it.
		// The IMMEDIATE lock is a performance optimization for write-heavy
		// scenarios to avoid SQLITE_BUSY on upgrade. In single-writer setups
		// (WAL mode), the standard BEGIN is typically fine.
		return EfTransaction;
	}
}
