using System.ComponentModel.DataAnnotations;

namespace WikiWikiWorld.Web.Configuration;

/// <summary>
/// Provides startup settings that control local hosting and storage paths.
/// </summary>
public sealed class ApplicationStartupOptions
{
	/// <summary>
	/// Gets the configuration section name for these options.
	/// </summary>
	public const string SectionName = "ApplicationStartup";

	/// <summary>
	/// Gets or sets the data root path used for database fallback storage and static file folders.
	/// </summary>
	public string DataPath { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the development HTTPS listener port.
	/// </summary>
	[Range(1, 65535)]
	public int DevelopmentHttpsPort { get; set; } = 7126;

	/// <summary>
	/// Gets or sets the development HTTP listener port.
	/// </summary>
	[Range(1, 65535)]
	public int DevelopmentHttpPort { get; set; } = 5097;

	/// <summary>
	/// Gets or sets the optional development HTTPS certificate subject name.
	/// </summary>
	public string? DevelopmentHttpsCertificateSubjectName { get; set; } = "en.localhost";
}
