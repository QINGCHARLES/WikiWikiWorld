using Microsoft.AspNetCore.Authorization;
using WikiWikiWorld.Web.Infrastructure;
using WikiWikiWorld.Web.Services;

namespace WikiWikiWorld.Web.Pages.Admin;

/// <summary>
/// Temporary administrator page for migrating legacy image files into the
/// revision-keyed storage layout. Restricted to a single operator account.
/// </summary>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="FileMigrationService">The file migration service.</param>
[Authorize(Policy = nameof(Permission.UseAdminTools))]
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
	/// Loads a dry-run scan of legacy files.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The page, or 404 when access is not permitted.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		if (!CanOperate(User.Identity?.Name))
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
		if (!CanOperate(User.Identity?.Name))
		{
			return NotFound();
		}

		AllowSearchEngineIndexingOfPage = false;
		Report = await FileMigrationService.MigrateAsync(CancellationToken);

		int Moved = Report.Items.Count(I => I.Status == FileMigrationStatus.Moved);
		int Failed = Report.Items.Count(I => I.Status == FileMigrationStatus.Failed);
		ResultMessage = SharedResource.Format("Migration complete: {0} moved, {1} failed. No database changes were required.", Moved, Failed);
		return Page();
	}

	/// <summary>Checks the single-operator restriction in addition to the admin policy enforced by the page.</summary>
	/// <param name="UserName">The signed-in account name.</param>
	/// <returns>Whether this account is the permitted file-migration operator.</returns>
	internal static bool CanOperate(string? UserName)
		=> string.Equals(UserName, AllowedUsername, StringComparison.OrdinalIgnoreCase);
}
