namespace WikiWikiWorld.Database;

public enum ConnectionMode
{
	Read,
	Write
}

public enum WriteDurability
{
	Normal, // synchronous=NORMAL (fast; latest commit may roll back on power loss)
	High    // synchronous=FULL   (slower; commit designed to survive power loss)
}

public interface IDbConnectionScope : IAsyncDisposable
{
	SqliteConnection Connection { get; }
}

public sealed record ConnectionOptions(
	int BusyTimeoutMs = 5_000,
	int CacheSizePages = -20_000,
	long MmapSizeBytes = 2_147_483_648,
	int PageSizeBytes = 8_192,
	int ReadMaxConcurrency = 0,
	int WriteMaxConcurrency = 1,
	int MaxRetryAttempts = 3,
	int RetryDelayMs = 50,
	int ShutdownTimeoutMs = 10_000,
	bool ProviderPooling = true)
{
	public int BusyTimeoutMs { get; init; } = BusyTimeoutMs;
	public int CacheSizePages { get; init; } = CacheSizePages;
	public long MmapSizeBytes { get; init; } = MmapSizeBytes;
	public int PageSizeBytes { get; init; } = PageSizeBytes;
	public int ReadMaxConcurrency { get; init; } = ReadMaxConcurrency <= 0 ? Environment.ProcessorCount : ReadMaxConcurrency;
	public int WriteMaxConcurrency { get; init; } = WriteMaxConcurrency <= 0 ? 1 : WriteMaxConcurrency;
	public int MaxRetryAttempts { get; init; } = MaxRetryAttempts;
	public int RetryDelayMs { get; init; } = RetryDelayMs;
	public int ShutdownTimeoutMs { get; init; } = ShutdownTimeoutMs;
	public bool ProviderPooling { get; init; } = ProviderPooling;
}

public interface IDatabaseConnectionFactory
{
	Task InitializeAsync(string DatabaseFilePath, CancellationToken CancellationToken = default);
	Task ShutdownAsync();

	Task<IDbConnectionScope> GetConnectionAsync(ConnectionMode Mode = ConnectionMode.Read, WriteDurability Durability = WriteDurability.Normal, CancellationToken CancellationToken = default);

	Task<T> ExecuteWithRetryAsync<T>(ConnectionMode Mode, Func<IDbConnection, Task<T>> Operation, WriteDurability Durability = WriteDurability.Normal, CancellationToken CancellationToken = default);
	Task ExecuteWithRetryAsync(ConnectionMode Mode, Func<IDbConnection, Task> Operation, WriteDurability Durability = WriteDurability.Normal, CancellationToken CancellationToken = default);

	Task<T> ExecuteWithRetryInTransactionAsync<T>(Func<IDbConnection, IDbTransaction, Task<T>> Operation, WriteDurability Durability = WriteDurability.Normal, CancellationToken CancellationToken = default);
	Task ExecuteWithRetryInTransactionAsync(Func<IDbConnection, IDbTransaction, Task> Operation, WriteDurability Durability = WriteDurability.Normal, CancellationToken CancellationToken = default);

	(int ActiveReadConnections, int ActiveWriteConnections) GetStats();
}

public sealed class DatabaseConnectionFactory : IDatabaseConnectionFactory, IAsyncDisposable
{
	private readonly ConnectionOptions Options;
	private readonly SemaphoreSlim InitializationLock = new(1, 1);

	private readonly SemaphoreSlim ReadGate;
	private readonly SemaphoreSlim WriteGate;

	// Single-use: never reset. If initialization fails, fail fast.
	private readonly TaskCompletionSource<bool> InitializationTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private string DatabaseFileName = string.Empty;
	private string ReadConnectionString = string.Empty;
	private string WriteConnectionString = string.Empty;

	// 1 = accepting; 0 = rejecting
	private volatile int AcceptingConnections = 0;

	private int ActiveReadConnections = 0;
	private int ActiveWriteConnections = 0;

	public DatabaseConnectionFactory(ConnectionOptions Options)
	{
		this.Options = Options ?? throw new ArgumentNullException(nameof(Options));
		ReadGate = new SemaphoreSlim(this.Options.ReadMaxConcurrency, this.Options.ReadMaxConcurrency);
		WriteGate = new SemaphoreSlim(this.Options.WriteMaxConcurrency, this.Options.WriteMaxConcurrency);
	}

	public async Task InitializeAsync(string DatabaseFilePath, CancellationToken CancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(DatabaseFilePath);

		await InitializationLock.WaitAsync(CancellationToken).ConfigureAwait(false);
		try
		{
			// If a previous init succeeded, do nothing (single-use).
			if (InitializationTcs.Task.IsCompletedSuccessfully)
			{
				return;
			}

			// If a previous init finished but failed/canceled, this factory is single-use: fail fast.
			if (InitializationTcs.Task.IsCompleted && !InitializationTcs.Task.IsCompletedSuccessfully)
			{
				throw new InvalidOperationException("DatabaseConnectionFactory initialization previously failed; this factory is single-use. Restart the app to re-initialize.");
			}

			string LocalDatabaseFileName = DatabaseFilePath;

			string LocalReadConnectionString = new SqliteConnectionStringBuilder
			{
				DataSource = LocalDatabaseFileName,
				Mode = SqliteOpenMode.ReadOnly,
				Cache = SqliteCacheMode.Private,
				Pooling = Options.ProviderPooling
			}.ToString();

			string LocalWriteConnectionString = new SqliteConnectionStringBuilder
			{
				DataSource = LocalDatabaseFileName,
				Mode = SqliteOpenMode.ReadWriteCreate,
				Cache = SqliteCacheMode.Private,
				Pooling = Options.ProviderPooling
			}.ToString();

			using SqliteConnection InitialConnection = new(LocalWriteConnectionString);
			await InitialConnection.OpenAsync(CancellationToken).ConfigureAwait(false);

			// Persistent DB PRAGMAs
			await ApplyDatabasePragmasAsync(InitialConnection, CancellationToken).ConfigureAwait(false);

			DatabaseFileName = LocalDatabaseFileName;
			ReadConnectionString = LocalReadConnectionString;
			WriteConnectionString = LocalWriteConnectionString;

			_ = Interlocked.Exchange(ref AcceptingConnections, 1);
			InitializationTcs.TrySetResult(true);
		}
		catch (Exception Exception)
		{
			InitializationTcs.TrySetException(Exception);
			throw new InvalidOperationException($"Failed to initialize database at '{DatabaseFilePath}'.", Exception);
		}
		finally
		{
			InitializationLock.Release();
		}
	}

	public async Task<IDbConnectionScope> GetConnectionAsync(ConnectionMode Mode = ConnectionMode.Read, WriteDurability Durability = WriteDurability.Normal, CancellationToken CancellationToken = default)
	{
		// Wait for initialization to complete (prevents visibility races)
		await InitializationTcs.Task.ConfigureAwait(false);

		if (AcceptingConnections is 0)
		{
			throw new InvalidOperationException("DatabaseConnectionFactory is not accepting new connections (shutting down).");
		}

		if (Mode is ConnectionMode.Read && Durability is not WriteDurability.Normal)
		{
			throw new ArgumentException("Durability can only be specified for write connections.", nameof(Durability));
		}

		SemaphoreSlim Gate = Mode is ConnectionMode.Read ? ReadGate : WriteGate;
		await Gate.WaitAsync(CancellationToken).ConfigureAwait(false);

		string ConnectionString = Mode is ConnectionMode.Read ? ReadConnectionString : WriteConnectionString;
		SqliteConnection Connection = new(ConnectionString);

		try
		{
			await Connection.OpenAsync(CancellationToken).ConfigureAwait(false);
			await ApplyConnectionPragmasAsync(Connection, CancellationToken).ConfigureAwait(false);

			if (Mode is ConnectionMode.Write)
			{
				await ApplyDurabilityPragmasAsync(Connection, Durability, CancellationToken).ConfigureAwait(false);
			}

			if (Mode is ConnectionMode.Read)
			{
				Interlocked.Increment(ref ActiveReadConnections);
			}
			else
			{
				Interlocked.Increment(ref ActiveWriteConnections);
			}

			// Capture to avoid fragile closure semantics.
			ConnectionMode CapturedMode = Mode;
			SemaphoreSlim CapturedGate = Gate;

			Func<ValueTask> OnDisposeAction = async () =>
			{
				try
				{
					// Intentionally swallow disposal exceptions to guarantee permit release.
					// For telemetry, hook logging here without rethrowing.
					try
					{
						await Connection.DisposeAsync().ConfigureAwait(false);
					}
					catch
					{
						// Ignored by design.
					}
				}
				finally
				{
					if (CapturedMode is ConnectionMode.Read)
					{
						Interlocked.Decrement(ref ActiveReadConnections);
					}
					else
					{
						Interlocked.Decrement(ref ActiveWriteConnections);
					}

					CapturedGate.Release();
				}
			};

			return new ConnectionScope(Connection, OnDisposeAction);
		}
		catch
		{
			Gate.Release();
			await Connection.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	public async Task<T> ExecuteWithRetryAsync<T>(ConnectionMode Mode, Func<IDbConnection, Task<T>> Operation, WriteDurability Durability = WriteDurability.Normal, CancellationToken CancellationToken = default)
	{
		return await ExecuteWithRetryCoreAsync(
			async () =>
			{
				await using IDbConnectionScope Scope = await GetConnectionAsync(Mode, Durability, CancellationToken).ConfigureAwait(false);
				return await Operation(Scope.Connection).ConfigureAwait(false);
			},
			Options.MaxRetryAttempts,
			Options.RetryDelayMs,
			CancellationToken).ConfigureAwait(false);
	}

	public async Task ExecuteWithRetryAsync(ConnectionMode Mode, Func<IDbConnection, Task> Operation, WriteDurability Durability = WriteDurability.Normal, CancellationToken CancellationToken = default)
	{
		await ExecuteWithRetryCoreAsync(
			async () =>
			{
				await using IDbConnectionScope Scope = await GetConnectionAsync(Mode, Durability, CancellationToken).ConfigureAwait(false);
				await Operation(Scope.Connection).ConfigureAwait(false);
				return true;
			},
			Options.MaxRetryAttempts,
			Options.RetryDelayMs,
			CancellationToken).ConfigureAwait(false);
	}

	public async Task<T> ExecuteWithRetryInTransactionAsync<T>(Func<IDbConnection, IDbTransaction, Task<T>> Operation, WriteDurability Durability = WriteDurability.Normal, CancellationToken CancellationToken = default)
	{
		return await ExecuteWithRetryAsync(ConnectionMode.Write, async Connection =>
		{
			using IDbTransaction Transaction = Connection.BeginTransaction();
			try
			{
				T Result = await Operation(Connection, Transaction).ConfigureAwait(false);
				Transaction.Commit();
				return Result;
			}
			catch (Exception OperationException)
			{
				try
				{
					Transaction.Rollback();
				}
				catch (Exception RollbackException)
				{
					throw new AggregateException(
						"Transaction failed and rollback also failed.",
						OperationException,
						RollbackException);
				}

				throw;
			}
		}, Durability, CancellationToken).ConfigureAwait(false);
	}

	public async Task ExecuteWithRetryInTransactionAsync(Func<IDbConnection, IDbTransaction, Task> Operation, WriteDurability Durability = WriteDurability.Normal, CancellationToken CancellationToken = default)
	{
		await ExecuteWithRetryAsync(ConnectionMode.Write, async Connection =>
		{
			using IDbTransaction Transaction = Connection.BeginTransaction();
			try
			{
				await Operation(Connection, Transaction).ConfigureAwait(false);
				Transaction.Commit();
			}
			catch (Exception OperationException)
			{
				try
				{
					Transaction.Rollback();
				}
				catch (Exception RollbackException)
				{
					throw new AggregateException(
						"Transaction failed and rollback also failed.",
						OperationException,
						RollbackException);
				}

				throw;
			}
		}, Durability, CancellationToken).ConfigureAwait(false);
	}

	public (int ActiveReadConnections, int ActiveWriteConnections) GetStats()
	{
		return (ActiveReadConnections, ActiveWriteConnections);
	}

	public async Task ShutdownAsync()
	{
		await ShutdownAsyncCore(throwOnTimeout: true).ConfigureAwait(false);
	}

	public async ValueTask DisposeAsync()
	{
		// Disposal should not throw; use a non-throwing shutdown path.
		await ShutdownAsyncCore(throwOnTimeout: false).ConfigureAwait(false);
		InitializationLock.Dispose();
		ReadGate.Dispose();
		WriteGate.Dispose();
	}

	private async Task ShutdownAsyncCore(bool throwOnTimeout)
	{
		await InitializationLock.WaitAsync().ConfigureAwait(false);
		try
		{
			// Stop accepting new loans
			_ = Interlocked.Exchange(ref AcceptingConnections, 0);

			// Unblock any waiters if init never completed
			if (!InitializationTcs.Task.IsCompleted)
			{
				InitializationTcs.TrySetException(new InvalidOperationException("Shutdown invoked before initialization completed."));
			}

			// Drain in-flight operations up to the configured timeout
			DateTimeOffset Deadline = DateTimeOffset.UtcNow.AddMilliseconds(Options.ShutdownTimeoutMs);
			while ((ActiveReadConnections > 0 || ActiveWriteConnections > 0) && DateTimeOffset.UtcNow < Deadline)
			{
				await Task.Delay(25).ConfigureAwait(false);
			}

			if ((ActiveReadConnections > 0 || ActiveWriteConnections > 0) && throwOnTimeout)
			{
				throw new InvalidOperationException("Shutdown timed out while waiting for active connections to complete.");
			}

			// Clear state (single-use; not intended for re-init)
			DatabaseFileName = string.Empty;
			ReadConnectionString = string.Empty;
			WriteConnectionString = string.Empty;
		}
		finally
		{
			InitializationLock.Release();
		}
	}

	private async Task ApplyDatabasePragmasAsync(SqliteConnection Connection, CancellationToken CancellationToken)
	{
		// Persistent settings
		List<string> Pragmas =
		[
			"PRAGMA journal_mode = WAL",
			"PRAGMA synchronous = NORMAL",
			$"PRAGMA page_size = {Options.PageSizeBytes}"
		];

		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = string.Join(";\n", Pragmas) + ";";
		await Command.ExecuteNonQueryAsync(CancellationToken).ConfigureAwait(false);
	}

	private async Task ApplyConnectionPragmasAsync(SqliteConnection Connection, CancellationToken CancellationToken)
	{
		// Per-connection settings
		List<string> Pragmas =
		[
			$"PRAGMA busy_timeout = {Options.BusyTimeoutMs}",
			$"PRAGMA cache_size = {Options.CacheSizePages}",
			$"PRAGMA mmap_size = {Options.MmapSizeBytes}",
			"PRAGMA temp_store = MEMORY",
			"PRAGMA foreign_keys = ON"
		];

		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = string.Join(";\n", Pragmas) + ";";
		await Command.ExecuteNonQueryAsync(CancellationToken).ConfigureAwait(false);
	}

	private static async Task ApplyDurabilityPragmasAsync(SqliteConnection Connection, WriteDurability Durability, CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(Connection);

		string SynchronousValue = Durability is WriteDurability.High ? "FULL" : "NORMAL";

		List<string> Pragmas =
		[
			$"PRAGMA synchronous = {SynchronousValue}"
		];

		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = string.Join(";\n", Pragmas) + ";";
		await Command.ExecuteNonQueryAsync(CancellationToken).ConfigureAwait(false);
	}

	private async Task<T> ExecuteWithRetryCoreAsync<T>(Func<Task<T>> Operation, int MaxAttempts, int BaseDelayMs, CancellationToken CancellationToken)
	{
		for (int Attempt = 1; ; Attempt++)
		{
			try
			{
				return await Operation().ConfigureAwait(false);
			}
			catch (SqliteException Exception) when (IsRetryable(Exception) && Attempt < MaxAttempts)
			{
				// Exponential backoff with full jitter
				int ExponentialMs = checked(BaseDelayMs << (Attempt - 1)); // Base * 2^(Attempt-1)
				int JitterMs = Random.Shared.Next(0, ExponentialMs);
				int DelayMs = ExponentialMs + JitterMs;

				await Task.Delay(DelayMs, CancellationToken).ConfigureAwait(false);
			}
		}
	}

	private static bool IsRetryable(SqliteException Exception)
	{
		// Primary BUSY
		if (Exception.SqliteErrorCode is 5)
		{
			return true;
		}

		// Extended BUSY variants: RECOVERY, SNAPSHOT, TIMEOUT
		int Extended = Exception.SqliteExtendedErrorCode;
		return Extended is 261 or 517 or 773;
	}
}

file sealed class ConnectionScope(SqliteConnection Connection, Func<ValueTask> OnDisposeAction) : IDbConnectionScope
{
	public SqliteConnection Connection { get; } = Connection;

	private int Disposed = 0;

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref Disposed, 1) is 0)
		{
			return OnDisposeAction();
		}

		return ValueTask.CompletedTask;
	}
}
