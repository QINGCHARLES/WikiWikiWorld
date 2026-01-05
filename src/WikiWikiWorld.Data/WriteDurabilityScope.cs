namespace WikiWikiWorld.Data;

/// <summary>
/// Provides an ambient scope to indicate that database writes within this scope
/// require high durability (PRAGMA synchronous = FULL).
/// </summary>
public sealed class WriteDurabilityScope : IDisposable
{
	private static readonly AsyncLocal<WriteDurability> CurrentDurability = new();

	private readonly WriteDurability PreviousDurability;
	private bool Disposed;

	/// <summary>
	/// Gets the current ambient write durability level. Defaults to Normal.
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
	/// </summary>
	/// <returns>A disposable scope that restores the previous durability on disposal.</returns>
	public static WriteDurabilityScope High() => new(WriteDurability.High);

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
