using Microsoft.Extensions.FileProviders;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies site isolation, cascade ordering, and startup snapshot behavior using real files.
/// </summary>
public sealed class SiteStylesheetCatalogTests
{
	/// <summary>
	/// Ensures optional overrides follow all defaults, belong only to their site, and are discovered once.
	/// </summary>
	[Fact]
	public void OverridesAreOrderedIsolatedAndSnapshotted()
	{
		string RootPath = Path.Combine(Path.GetTempPath(), $"wiki-css-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Combine(RootPath, "css/sites/1/core"));
		try
		{
			File.WriteAllText(Path.Combine(RootPath, "css/sites/1/core/variables.css"), ":root { --accent: red; }");
			File.WriteAllText(Path.Combine(RootPath, "css/sites/1/core/colors.css"), ":root { --brand: blue; }");
			using PhysicalFileProvider Files = new(RootPath);
			SiteStylesheetCatalog Catalog = new(Files, [1, 2, 1]);
			IReadOnlyList<string> Defaults = Catalog.GetPaths(99);
			Assert.Equal(Defaults, Catalog.GetPaths(2));
			Assert.Equal(Defaults, Catalog.GetPaths(1).Take(Defaults.Count));
			Assert.Equal(["~/css/sites/1/core/colors.css", "~/css/sites/1/core/variables.css"], Catalog.GetPaths(1).Skip(Defaults.Count));
			Assert.Contains("~/css/components/pullquote.css", Defaults);

			File.WriteAllText(Path.Combine(RootPath, "css/sites/1/core/base.css"), "body { color: green; }");
			Assert.DoesNotContain("~/css/sites/1/core/base.css", Catalog.GetPaths(1));
			SiteStylesheetCatalog RestartedCatalog = new(Files, [1]);
			Assert.Contains("~/css/sites/1/core/base.css", RestartedCatalog.GetPaths(1));
		}
		finally
		{
			Directory.Delete(RootPath, recursive: true);
		}
	}
}
