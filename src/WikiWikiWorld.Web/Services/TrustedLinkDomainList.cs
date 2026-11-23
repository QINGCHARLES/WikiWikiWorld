using System.Collections;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// The domains whose external links are followed (no <c>rel="nofollow"</c>), held in memory for the Markdown pipelines.
/// </summary>
/// <remarks>
/// <para>
/// The cached pipelines receive this object once, as an <see cref="IReadOnlyList{T}"/>, and read it on every render. Each
/// member reads the current snapshot, which <see cref="ReloadAsync"/> swaps after an administrator confirms or withdraws a
/// domain, so the change applies to the next page rendered without a restart. Loaded at startup from
/// <c>TrustedLinkDomains</c> rows with status <see cref="TrustedLinkDomainStatus.Trusted"/>.
/// </para>
/// <para>
/// Assumes one application instance: a second instance would keep its own snapshot until its next reload.
/// </para>
/// </remarks>
public sealed class TrustedLinkDomainList : IReadOnlyList<string>
{
	/// <summary>The current snapshot; replaced whole, never mutated.</summary>
	private volatile string[] Domains = [];

	/// <summary>Serializes reloads so a reload that read older rows cannot overwrite a newer snapshot.</summary>
	private readonly SemaphoreSlim ReloadGate = new(1, 1);

	/// <inheritdoc/>
	public string this[int Index] => Domains[Index];

	/// <inheritdoc/>
	public int Count => Domains.Length;

	/// <inheritdoc/>
	public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)Domains).GetEnumerator();

	/// <inheritdoc/>
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <summary>
	/// Creates a list holding fixed domains, for tests and tools that render without a database.
	/// </summary>
	/// <param name="TrustedDomains">Normalized domains to trust.</param>
	/// <returns>A list whose snapshot is <paramref name="TrustedDomains"/>.</returns>
	public static TrustedLinkDomainList From(IEnumerable<string> TrustedDomains)
	{
		TrustedLinkDomainList List = new();
		List.Replace(TrustedDomains);
		return List;
	}

	/// <summary>
	/// Replaces the snapshot directly, for tests and tools that render without a database.
	/// </summary>
	/// <param name="TrustedDomains">Normalized domains to trust.</param>
	public void Replace(IEnumerable<string> TrustedDomains)
	{
		ArgumentNullException.ThrowIfNull(TrustedDomains);
		Domains = [.. TrustedDomains];
	}

	/// <summary>
	/// Reads every trusted domain from the database and swaps it in. Call after a change has been committed.
	/// </summary>
	/// <param name="Context">A database context; the table is global, so any site scope works.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A task that completes when the new snapshot is in place.</returns>
	public async Task ReloadAsync(WikiWikiWorldDbContext Context, CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(Context);

		await ReloadGate.WaitAsync(CancellationToken);
		try
		{
			string[] Fresh = await Context.TrustedLinkDomains
				.Where(Row => Row.Status == TrustedLinkDomainStatus.Trusted)
				.OrderBy(Row => Row.Domain)
				.Select(Row => Row.Domain)
				.ToArrayAsync(CancellationToken);
			Domains = Fresh;
		}
		finally
		{
			ReloadGate.Release();
		}
	}
}
