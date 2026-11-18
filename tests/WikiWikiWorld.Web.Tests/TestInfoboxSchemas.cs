using Microsoft.Extensions.Logging.Abstractions;
using WikiWikiWorld.Web.Services;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Supplies the real site infobox schemas, copied into the test output, so renders in tests use the same field
/// order and labels as the site and a broken schema file fails the tests.
/// </summary>
internal static class TestInfoboxSchemas
{
	/// <summary>The site whose schema the infobox tests render against (Magazedia).</summary>
	internal const int SiteId = 1;

	/// <summary>Gets the catalog loaded from the copied <c>InfoboxSchemas</c> folder, rejecting unconfigured sites as production does.</summary>
	internal static InfoboxSchemaCatalog Catalog { get; } = InfoboxSchemaCatalog.Load(
		Path.Combine(AppContext.BaseDirectory, InfoboxSchemaCatalog.FolderName),
		[SiteId],
		RejectUnconfiguredSites: true,
		NullLogger.Instance);
}
