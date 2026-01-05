namespace WikiWikiWorld.Data;

/// <summary>
/// Specifies the durability level for SQLite write operations.
/// </summary>
public enum WriteDurability
{
	/// <summary>
	/// Uses PRAGMA synchronous = NORMAL. Fast; latest commit may roll back on power loss.
	/// Suitable for most operations (article edits, comments, etc.)
	/// </summary>
	Normal,

	/// <summary>
	/// Uses PRAGMA synchronous = FULL. Slower; commit designed to survive power loss.
	/// Use for security-critical operations (password changes, role updates, financial data).
	/// </summary>
	High
}
