using System.ComponentModel.DataAnnotations;

namespace WikiWikiWorld.Web.Configuration;

/// <summary>
/// Configuration options for file storage.
/// </summary>
public sealed class FileStorageOptions
{
    /// <summary>
    /// Gets or sets the path where site files are stored.
    /// </summary>
    public string SiteFilesPath { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the age in minutes after which unclaimed staged upload files can be deleted.
	/// </summary>
	[Range(1, int.MaxValue)]
	public int StaleTempFileAgeMinutes { get; set; } = 60;
}
