using Microsoft.AspNetCore.Authorization;
using WikiWikiWorld.Web.Services;

namespace WikiWikiWorld.Web.Pages.Admin;

/// <summary>
/// Temporary administrator page for migrating legacy image files into the
/// revision-keyed storage layout. Restricted to a single operator account.
/// </summary>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="FileMigrationService">The file migration service.</param>
[Authorize]
public sealed class MigrateFilesModel(
	SiteResolverService SiteResolverService,
	FileMigrationService FileMigrationService) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// The only username permitted to access this maintenance page.
	/// </summary>
	private const string AllowedUsername = "qingcharles";

	/// <summary>
	/// Gets the migration report shown on the page, if access is permitted.
	/// </summary>
	public FileMigrationReport? Report { get; private set; }

	/// <summary>
	/// Gets a message describing the result of a migration run.
	/// </summary>
	public string? ResultMessage { get; private set; }

	/// <summary>
	/// Gets a value indicating whether files were actually moved on the last action.
	/// </summary>
	public bool MigrationRan { get; private set; }

	/// <summary>
	/// Loads a dry-run scan of legacy files.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The page, or 404 when access is not permitted.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		if (!IsAllowed())
		{
			return NotFound();
		}

		AllowSearchEngineIndexingOfPage = false;
		Report = await FileMigrationService.ScanAsync(CancellationToken);
		return Page();
	}

	/// <summary>
	/// Performs the migration, moving legacy files into the revision-keyed layout.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The page with results, or 404 when access is not permitted.</returns>
	public async Task<IActionResult> OnPostMigrateAsync(CancellationToken CancellationToken)
	{
		if (!IsAllowed())
		{
			return NotFound();
		}

		AllowSearchEngineIndexingOfPage = false;
		MigrationRan = true;
		Report = await FileMigrationService.MigrateAsync(CancellationToken);

		int Moved = Report.Items.Count(I => I.Status == FileMigrationStatus.Moved);
		int Failed = Report.Items.Count(I => I.Status == FileMigrationStatus.Failed);
		ResultMessage = $"Migration complete: {Moved} moved, {Failed} failed. No database changes were required.";
		return Page();
	}

	private bool IsAllowed()
		=> string.Equals(User.Identity?.Name, AllowedUsername, StringComparison.OrdinalIgnoreCase);
}
