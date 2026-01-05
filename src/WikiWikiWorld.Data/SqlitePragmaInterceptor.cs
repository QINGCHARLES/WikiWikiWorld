namespace WikiWikiWorld.Data;

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;

/// <summary>
/// Interceptor that applies SQLite PRAGMAs when a connection is opened.
/// </summary>
/// <param name="BusyTimeoutMs">Maximum time to wait for database locks (milliseconds).</param>
/// <param name="CacheSizePages">Cache size. Negative = KB, positive = pages.</param>
/// <param name="MmapSizeBytes">Memory-mapped I/O size in bytes.</param>
public sealed class SqlitePragmaInterceptor(
	int BusyTimeoutMs = 5_000,
	int CacheSizePages = -20_000,
	long MmapSizeBytes = 2_147_483_648) : DbConnectionInterceptor
{
	/// <inheritdoc/>
	public override void ConnectionOpened(DbConnection Connection, ConnectionEndEventData EventData)
	{
		ApplyPragmas((SqliteConnection)Connection);
		base.ConnectionOpened(Connection, EventData);
	}

	/// <inheritdoc/>
	public override async Task ConnectionOpenedAsync(
		DbConnection Connection,
		ConnectionEndEventData EventData,
		CancellationToken CancellationToken = default)
	{
		await ApplyPragmasAsync((SqliteConnection)Connection, CancellationToken).ConfigureAwait(false);
		await base.ConnectionOpenedAsync(Connection, EventData, CancellationToken).ConfigureAwait(false);
	}

	private void ApplyPragmas(SqliteConnection Connection)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = BuildPragmasSql();
		Command.ExecuteNonQuery();
	}

	private async Task ApplyPragmasAsync(SqliteConnection Connection, CancellationToken CancellationToken)
	{
		await using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = BuildPragmasSql();
		await Command.ExecuteNonQueryAsync(CancellationToken).ConfigureAwait(false);
	}

	private string BuildPragmasSql() =>
		$"""
		PRAGMA busy_timeout = {BusyTimeoutMs};
		PRAGMA cache_size = {CacheSizePages};
		PRAGMA mmap_size = {MmapSizeBytes};
		PRAGMA temp_store = MEMORY;
		PRAGMA foreign_keys = ON;
		""";
}
