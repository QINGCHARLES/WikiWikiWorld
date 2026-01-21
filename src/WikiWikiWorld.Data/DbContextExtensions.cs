using System.Data.Common;
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
	/// Begins a transaction. For SQLite, executes "BEGIN IMMEDIATE" to prevent upgrade deadlocks.
	/// For other providers, falls back to the default behavior.
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

		// SQLite-specific optimization: BEGIN IMMEDIATE
		await Database.OpenConnectionAsync(CancellationToken);
		
		// We execute the locking command manually
		await Database.ExecuteSqlRawAsync("BEGIN IMMEDIATE;", CancellationToken);

		// Return a wrapper that manages this manual transaction
		return new SqliteImmediateTransaction(Database);
	}

	/// <summary>
	/// Custom transaction wrapper for SQLite IMMEDIATE transactions.
	/// </summary>
	private sealed class SqliteImmediateTransaction(DatabaseFacade Database) : IDbContextTransaction
	{
		private bool IsCommitted;
		private bool IsDisposed;

		public Guid TransactionId { get; } = Guid.NewGuid();

		/// <inheritdoc/>
		public void Commit()
		{
			CommitAsync(CancellationToken.None).GetAwaiter().GetResult();
		}

		/// <inheritdoc/>
		public async Task CommitAsync(CancellationToken CancellationToken = default)
		{
			if (IsCommitted || IsDisposed) throw new InvalidOperationException("Transaction already committed or disposed.");
			
			await Database.ExecuteSqlRawAsync("COMMIT;", CancellationToken);
			IsCommitted = true;
		}

		/// <inheritdoc/>
		public void Rollback()
		{
			RollbackAsync(CancellationToken.None).GetAwaiter().GetResult();
		}

		/// <inheritdoc/>
		public async Task RollbackAsync(CancellationToken CancellationToken = default)
		{
			if (IsCommitted || IsDisposed) return; // Already done

			try
			{
				await Database.ExecuteSqlRawAsync("ROLLBACK;", CancellationToken);
			}
			catch (Exception)
			{
				// Ignore rollback errors (connection might be closed etc)
			}
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			if (!IsDisposed)
			{
				if (!IsCommitted)
				{
					try
					{
						// Synchronous dispose fallback
						Database.ExecuteSqlRaw("ROLLBACK;");
					}
					catch
					{
						// Best effort
					}
				}
				IsDisposed = true;
			}
		}

		/// <inheritdoc/>
		public async ValueTask DisposeAsync()
		{
			if (!IsDisposed)
			{
				if (!IsCommitted)
				{
					await RollbackAsync();
				}
				IsDisposed = true;
			}
		}
	}
}
