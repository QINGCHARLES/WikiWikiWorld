using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Holds every site's infobox types, read once at startup from <c>InfoboxSchemas/*.json</c>.
/// Changes to the files take effect on the next restart.
/// </summary>
public sealed partial class InfoboxSchemaCatalog
{
	/// <summary>The folder, relative to the content root, that holds one schema file per site.</summary>
	public const string FolderName = "InfoboxSchemas";

	/// <summary>The culture whose label every field must define and every other culture falls back to.</summary>
	public const string FallbackCulture = "en";

	/// <summary>
	/// Reads schema files strictly: an unknown property (such as a misspelt <c>Repeatible</c>) or a missing
	/// required one fails the load instead of being silently ignored. Comments are allowed for maintainers' notes.
	/// </summary>
	private static readonly JsonSerializerOptions ReadOptions = new()
	{
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowDuplicateProperties = false,
	};

	private readonly Dictionary<int, InfoboxSiteSchema> Sites;

	/// <summary>Wraps already validated site schemas.</summary>
	/// <param name="Sites">Validated schemas keyed by site.</param>
	private InfoboxSchemaCatalog(Dictionary<int, InfoboxSiteSchema> Sites) => this.Sites = Sites;

	/// <summary>Gets the identifiers of the sites that have a schema file.</summary>
	public IReadOnlyCollection<int> SiteIds => Sites.Keys;

	/// <summary>
	/// Looks up an infobox type for a site.
	/// </summary>
	/// <param name="SiteId">The site resolved for the current request.</param>
	/// <param name="TypeName">The type name written after <c>{{Infobox</c>, matched case-insensitively.</param>
	/// <param name="Type">The matching type when found.</param>
	/// <returns><see langword="true"/> when the site has a schema file defining that type.</returns>
	public bool TryGetType(int SiteId, string TypeName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out InfoboxTypeSchema? Type)
	{
		Type = null;
		return Sites.TryGetValue(SiteId, out InfoboxSiteSchema? Site) && Site.Types.TryGetValue(TypeName, out Type);
	}

	/// <summary>Lists the infobox types a site defines, for messages that tell authors what they can use.</summary>
	/// <param name="SiteId">The site resolved for the current request.</param>
	/// <returns>The type names in file order; empty when the site has no schema file.</returns>
	public IReadOnlyCollection<string> GetTypeNames(int SiteId) =>
		Sites.TryGetValue(SiteId, out InfoboxSiteSchema? Site) ? [.. Site.Types.Keys] : [];

	/// <summary>
	/// Reads and validates every <c>*.json</c> file in the schema folder. A missing folder yields an empty catalog,
	/// so sites without schemas still render infoboxes with their raw keys.
	/// </summary>
	/// <param name="FolderPath">The absolute path of the schema folder.</param>
	/// <param name="ConfiguredSiteIds">Site identifiers from <see cref="SiteConfiguration.Sites"/>.</param>
	/// <param name="RejectUnconfiguredSites">
	/// <see langword="true"/> to fail when a file names a site that is not configured (production);
	/// <see langword="false"/> to log a warning and skip that file (development, where some sites are not set up).
	/// </param>
	/// <param name="Logger">Receives the warning for a skipped file.</param>
	/// <returns>The validated catalog.</returns>
	/// <exception cref="InvalidOperationException">
	/// A file is malformed or invalid, two files claim the same site, or a file names an unconfigured site
	/// while <paramref name="RejectUnconfiguredSites"/> is set. The message names the file.
	/// </exception>
	public static InfoboxSchemaCatalog Load(string FolderPath, IReadOnlyCollection<int> ConfiguredSiteIds, bool RejectUnconfiguredSites, ILogger Logger)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(FolderPath);
		ArgumentNullException.ThrowIfNull(ConfiguredSiteIds);
		ArgumentNullException.ThrowIfNull(Logger);

		Dictionary<int, InfoboxSiteSchema> Sites = [];
		Dictionary<int, string> SourceFiles = [];
		if (!Directory.Exists(FolderPath))
		{
			return new(Sites);
		}

		foreach (string FilePath in Directory.EnumerateFiles(FolderPath, "*.json").Order(StringComparer.OrdinalIgnoreCase))
		{
			string FileName = Path.GetFileName(FilePath);
			InfoboxSiteSchema Site = ReadFile(FilePath, FileName);

			if (!ConfiguredSiteIds.Contains(Site.SiteId))
			{
				if (RejectUnconfiguredSites)
				{
					throw new InvalidOperationException($"Infobox schema {FileName} names SiteId {Site.SiteId}, which is not in SiteConfiguration.Sites.");
				}
				LogUnconfiguredSite(Logger, FileName, Site.SiteId);
				continue;
			}

			if (SourceFiles.TryGetValue(Site.SiteId, out string? FirstFile))
			{
				throw new InvalidOperationException($"Infobox schemas {FirstFile} and {FileName} both name SiteId {Site.SiteId}; each site needs exactly one file.");
			}

			SourceFiles.Add(Site.SiteId, FileName);
			Sites.Add(Site.SiteId, Site);
		}

		return new(Sites);
	}

	/// <summary>
	/// Deserializes one file and returns a copy whose type and label lookups ignore case, after checking the rules
	/// JSON deserialization cannot express.
	/// </summary>
	/// <param name="FilePath">The file to read.</param>
	/// <param name="FileName">The file name used in error messages.</param>
	/// <returns>The validated, case-insensitive schema.</returns>
	/// <exception cref="InvalidOperationException">The file is not valid JSON for the schema shape or breaks a rule.</exception>
	private static InfoboxSiteSchema ReadFile(string FilePath, string FileName)
	{
		InfoboxSiteSchema? Raw;
		try
		{
			using FileStream Stream = File.OpenRead(FilePath);
			Raw = JsonSerializer.Deserialize<InfoboxSiteSchema>(Stream, ReadOptions);
		}
		catch (JsonException Exception)
		{
			throw new InvalidOperationException($"Infobox schema {FileName} is not valid: {Exception.Message}", Exception);
		}

		if (Raw is null)
		{
			throw new InvalidOperationException($"Infobox schema {FileName} is empty.");
		}
		if (Raw.Types.Count == 0)
		{
			throw new InvalidOperationException($"Infobox schema {FileName} defines no types.");
		}

		Dictionary<string, InfoboxTypeSchema> Types = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string TypeName, InfoboxTypeSchema Type) in Raw.Types)
		{
			string Where = $"Infobox schema {FileName}, type \"{TypeName}\"";
			if (!TypeNamePattern().IsMatch(TypeName))
			{
				throw new InvalidOperationException($"{Where}: type names must be letters and digits, starting with a letter.");
			}
			if (Type.Fields.Count == 0)
			{
				throw new InvalidOperationException($"{Where} defines no fields.");
			}

			HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase);
			List<InfoboxFieldSchema> Fields = [with(capacity: Type.Fields.Count)];
			foreach (InfoboxFieldSchema Field in Type.Fields)
			{
				if (string.IsNullOrWhiteSpace(Field.Key) || Field.Key.Any(char.IsWhiteSpace) || Field.Key.Contains('=', StringComparison.Ordinal))
				{
					throw new InvalidOperationException($"{Where}: field key \"{Field.Key}\" must be non-empty with no spaces or '='.");
				}
				if (!Keys.Add(Field.Key))
				{
					throw new InvalidOperationException($"{Where}: field \"{Field.Key}\" is defined twice.");
				}

				Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase);
				foreach ((string Culture, string Label) in Field.Labels)
				{
					if (string.IsNullOrWhiteSpace(Label))
					{
						throw new InvalidOperationException($"{Where}: field \"{Field.Key}\" has an empty \"{Culture}\" label.");
					}
					if (!Labels.TryAdd(Culture, Label))
					{
						throw new InvalidOperationException($"{Where}: field \"{Field.Key}\" has two \"{Culture}\" labels.");
					}
				}
				if (!Labels.ContainsKey(FallbackCulture))
				{
					throw new InvalidOperationException($"{Where}: field \"{Field.Key}\" needs an \"{FallbackCulture}\" label.");
				}

				Fields.Add(Field with { Labels = Labels });
			}

			if (!Types.TryAdd(TypeName, Type with { Fields = Fields.AsReadOnly() }))
			{
				throw new InvalidOperationException($"{Where} is defined twice (type names ignore case).");
			}
		}

		return Raw with { Types = Types };
	}

	/// <summary>Matches a type name that can follow <c>{{Infobox</c> without spaces or punctuation.</summary>
	/// <returns>The compiled pattern.</returns>
	[GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$")]
	private static partial Regex TypeNamePattern();

	/// <summary>Records that a schema file was skipped because its site is not configured in this environment.</summary>
	/// <param name="Logger">The startup logger.</param>
	/// <param name="FileName">The skipped file.</param>
	/// <param name="SiteId">The unconfigured site it names.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Infobox schema {FileName} skipped: SiteId {SiteId} is not in SiteConfiguration.Sites for this environment.")]
	private static partial void LogUnconfiguredSite(ILogger Logger, string FileName, int SiteId);
}
