namespace WikiWikiWorld.Data;

using Microsoft.Data.Sqlite;

/// <summary>
/// Initializes SQLite database-level settings (WAL mode, page size).
/// Call once at application startup before any DbContext usage.
/// </summary>
public static class SqliteInitializer
{
	/// <summary>
	/// Applies database-level PRAGMAs that persist across connections.
	/// </summary>
	/// <param name="ConnectionString">The SQLite connection string.</param>
	public static void Initialize(string ConnectionString)
	{
		using SqliteConnection Connection = new(ConnectionString);
		Connection.Open();

		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = """
			PRAGMA journal_mode = WAL;
			PRAGMA synchronous = NORMAL;
			PRAGMA page_size = 8192;
			""";
		Command.ExecuteNonQuery();
	}

	/// <summary>
	/// Applies database-level PRAGMAs that persist across connections.
	/// </summary>
	/// <param name="ConnectionString">The SQLite connection string.</param>
	/// <param name="CancellationToken">Cancellation token.</param>
	public static async Task InitializeAsync(string ConnectionString, CancellationToken CancellationToken = default)
	{
		await using SqliteConnection Connection = new(ConnectionString);
		await Connection.OpenAsync(CancellationToken).ConfigureAwait(false);

		await using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = """
			PRAGMA journal_mode = WAL;
			PRAGMA synchronous = NORMAL;
			PRAGMA page_size = 8192;
			""";
		await Command.ExecuteNonQueryAsync(CancellationToken).ConfigureAwait(false);
	}
}
