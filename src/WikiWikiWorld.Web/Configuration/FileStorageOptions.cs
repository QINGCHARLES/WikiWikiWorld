namespace WikiWikiWorld.Web.Configuration;

/// <summary>
/// Configuration options for file storage.
/// </summary>
public class FileStorageOptions
{
    /// <summary>
    /// Gets or sets the path where site files are stored.
    /// </summary>
    public string SiteFilesPath { get; set; } = string.Empty;
}
