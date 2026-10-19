namespace WikiWikiWorld.Data;

/// <summary>
/// Specifies the durability level for SQLite write operations.
/// See doc/write-durability.md for the full design and usage guidance.
/// </summary>
public enum WriteDurability
{
	/// <summary>
	/// Uses PRAGMA synchronous = FULL. Commit is designed to survive power loss.
	/// This is the DEFAULT: any write without an explicit scope runs at this level,
	/// so forgetting a scope costs performance, never durability.
	/// </summary>
	High = 0,

	/// <summary>
	/// Uses PRAGMA synchronous = NORMAL. Faster (skips the WAL fsync at commit);
	/// the most recent commit(s) may be lost on power failure, but the database
	/// cannot be corrupted. Opt in via <see cref="WriteDurabilityScope.Normal"/> for
	/// high-volume ephemeral writes only (likes, upvotes, notifications, counters).
	/// </summary>
	Normal = 1
}
