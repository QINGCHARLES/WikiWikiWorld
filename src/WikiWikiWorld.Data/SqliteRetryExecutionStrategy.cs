namespace WikiWikiWorld.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// Custom execution strategy for SQLite that retries on BUSY/LOCKED errors
/// using exponential backoff with jitter.
/// </summary>
public sealed class SqliteRetryExecutionStrategy : ExecutionStrategy
{
	private new const int DefaultMaxRetryCount = 3;
	private static readonly TimeSpan DefaultMaxRetryDelay = TimeSpan.FromSeconds(2);

	/// <summary>
	/// Initializes a new instance with default retry settings.
	/// </summary>
	/// <param name="Dependencies">The execution strategy dependencies.</param>
	public SqliteRetryExecutionStrategy(ExecutionStrategyDependencies Dependencies)
		: base(Dependencies, DefaultMaxRetryCount, DefaultMaxRetryDelay)
	{
	}

	/// <summary>
	/// Initializes a new instance with custom retry count.
	/// </summary>
	/// <param name="Dependencies">The execution strategy dependencies.</param>
	/// <param name="MaxRetryCount">Maximum number of retry attempts.</param>
	public SqliteRetryExecutionStrategy(ExecutionStrategyDependencies Dependencies, int MaxRetryCount)
		: base(Dependencies, MaxRetryCount, DefaultMaxRetryDelay)
	{
	}

	/// <inheritdoc/>
	protected override bool ShouldRetryOn(Exception Exception)
	{
		// Unwrap aggregate exceptions
		if (Exception is AggregateException Aggregate)
		{
			foreach (Exception Inner in Aggregate.InnerExceptions)
			{
				if (ShouldRetryOn(Inner))
				{
					return true;
				}
			}
			return false;
		}

		// Check for retryable SQLite errors
		if (Exception is SqliteException SqliteEx)
		{
		// Primary BUSY (5) or LOCKED (6)
			if (SqliteEx.SqliteErrorCode is 5 or 6)
			{
				return true;
			}

			// Extended BUSY variants: RECOVERY (261), SNAPSHOT (517), TIMEOUT (773)
			// Extended LOCKED variant: SHAREDCACHE (262)
			int Extended = SqliteEx.SqliteExtendedErrorCode;
			return Extended is 261 or 262 or 517 or 773;
		}

		return false;
	}

	/// <inheritdoc/>
	protected override TimeSpan? GetNextDelay(Exception LastException)
	{
		TimeSpan? BaseDelay = base.GetNextDelay(LastException);
		if (BaseDelay is null)
		{
			return null;
		}

		// Add jitter (0% to 100% of base delay) to prevent thundering herd
		int JitterMs = Random.Shared.Next(0, (int)BaseDelay.Value.TotalMilliseconds);
		return BaseDelay.Value + TimeSpan.FromMilliseconds(JitterMs);
	}
}
