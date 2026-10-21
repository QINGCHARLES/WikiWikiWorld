using Microsoft.Extensions.FileProviders;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Captures shared stylesheets and optional per-site overrides once during application startup.
/// </summary>
public sealed class SiteStylesheetCatalog
{
	private static readonly string[] RelativePaths =
	[
		"core/reset.css",
		"core/colors.css",
		"core/variables.css",
		"core/typography.css",
		"core/base.css",
		"layout/header.css",
		"layout/footer.css",
		"components/buttons.css",
		"components/tables.css",
		"components/forms.css",
		"components/cards.css",
		"components/carousel.css",
		"components/navigation.css",
		"components/avatar.css",
		"components/timeline.css",
		"components/pullquote.css",
		"components/issue-credits.css",
		"components/issue-contents.css",
		"components/stub.css",
		"components/media.css",
		"components/file-info.css",
		"pages/article.css",
		"pages/search.css",
		"pages/static.css",
		"pages/talk.css",
		"pages/account.css",
	];

	private readonly IReadOnlyList<string> SharedPaths = Array.AsReadOnly(RelativePaths.Select(Path => $"~/css/{Path}").ToArray());
	private readonly Dictionary<int, IReadOnlyList<string>> SitePaths = [];

	/// <summary>
	/// Builds ordered stylesheet lists for configured sites without retaining the file provider.
	/// All shared files precede all existing overrides; changes to file membership require a restart.
	/// </summary>
	/// <param name="WebRootFiles">The provider serving the application's public files.</param>
	/// <param name="SiteIds">Configured site identifiers whose optional overrides should be discovered.</param>
	public SiteStylesheetCatalog(IFileProvider WebRootFiles, IEnumerable<int> SiteIds)
	{
		foreach (int SiteId in SiteIds.Distinct())
		{
			List<string> Paths = [.. SharedPaths];
			foreach (string RelativePath in RelativePaths)
			{
				string OverridePath = $"css/sites/{SiteId.ToString(CultureInfo.InvariantCulture)}/{RelativePath}";
				if (WebRootFiles.GetFileInfo(OverridePath) is { Exists: true, IsDirectory: false })
				{
					Paths.Add($"~/{OverridePath}");
				}
			}
			SitePaths.Add(SiteId, Paths.AsReadOnly());
		}
	}

	/// <summary>
	/// Returns the startup snapshot for a site, or shared defaults when the site is unknown.
	/// </summary>
	/// <param name="SiteId">The site resolved for the current request.</param>
	/// <returns>Application-relative stylesheet paths in cascade order, without filesystem access.</returns>
	public IReadOnlyList<string> GetPaths(int SiteId)
		=> SitePaths.TryGetValue(SiteId, out IReadOnlyList<string>? Paths) ? Paths : SharedPaths;
}
