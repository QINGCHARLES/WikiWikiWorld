namespace WikiWikiWorld.Data;

/// <summary>
/// Provides an ambient scope controlling the durability level of database writes.
/// The ambient default (no scope) is <see cref="WriteDurability.High"/> — writes are
/// power-loss safe unless a code path explicitly opts down via <see cref="Normal"/>.
/// See doc/write-durability.md for usage guidance.
/// </summary>
/// <remarks>
/// The scope must be entered BEFORE the write begins — wrap the whole unit of work
/// (including BeginTransactionAsync / the execution strategy lambda), not just the
/// SaveChangesAsync call, because the durability pragma is applied when the
/// connection opens and when the transaction starts.
/// </remarks>
public sealed class WriteDurabilityScope : IDisposable
{
	private static readonly AsyncLocal<WriteDurability> CurrentDurability = new();

	private readonly WriteDurability PreviousDurability;
	private bool Disposed;

	/// <summary>
	/// Gets the current ambient write durability level. Defaults to <see cref="WriteDurability.High"/>.
	/// </summary>
	public static WriteDurability Current => CurrentDurability.Value;

	/// <summary>
	/// Initializes a new durability scope with the specified level.
	/// </summary>
	/// <param name="Durability">The durability level for this scope.</param>
	public WriteDurabilityScope(WriteDurability Durability)
	{
		PreviousDurability = CurrentDurability.Value;
		CurrentDurability.Value = Durability;
	}

	/// <summary>
	/// Creates a high-durability scope (PRAGMA synchronous = FULL).
	/// Rarely needed — High is already the ambient default. Use only to re-raise
	/// durability inside an enclosing <see cref="Normal"/> scope.
	/// </summary>
	/// <returns>A disposable scope that restores the previous durability on disposal.</returns>
	public static WriteDurabilityScope High() => new(WriteDurability.High);

	/// <summary>
	/// Creates a reduced-durability scope (PRAGMA synchronous = NORMAL) for
	/// high-volume ephemeral writes (likes, upvotes, notifications, counters).
	/// Skips the per-commit WAL fsync; a power failure may lose the most recent
	/// commit(s) but can never corrupt the database.
	/// </summary>
	/// <returns>A disposable scope that restores the previous durability on disposal.</returns>
	public static WriteDurabilityScope Normal() => new(WriteDurability.Normal);

	/// <inheritdoc/>
	public void Dispose()
	{
		if (!Disposed)
		{
			CurrentDurability.Value = PreviousDurability;
			Disposed = true;
		}
	}
}
