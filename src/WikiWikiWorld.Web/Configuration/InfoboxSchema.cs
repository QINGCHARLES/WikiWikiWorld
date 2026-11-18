namespace WikiWikiWorld.Web.Configuration;

/// <summary>
/// One site's infobox schema file from <c>InfoboxSchemas/*.json</c>: the infobox types that site's articles may use.
/// </summary>
public sealed record class InfoboxSiteSchema
{
	/// <summary>Gets the site these types belong to; must match a <see cref="SiteInfo.SiteId"/>.</summary>
	public required int SiteId { get; init; }

	/// <summary>Gets the types keyed by the name written after <c>{{Infobox</c>, compared case-insensitively once loaded.</summary>
	public required IReadOnlyDictionary<string, InfoboxTypeSchema> Types { get; init; }
}

/// <summary>
/// One infobox type, such as <c>Publication</c>: the fields it defines, in the order they render.
/// </summary>
public sealed record class InfoboxTypeSchema
{
	/// <summary>Gets an editor-facing explanation of what the type is for; not rendered on article pages.</summary>
	public string? Description { get; init; }

	/// <summary>Gets the defined fields in display order.</summary>
	public required IReadOnlyList<InfoboxFieldSchema> Fields { get; init; }
}

/// <summary>
/// One field of an infobox type: its authored key, display labels per culture and authoring guidance.
/// </summary>
public sealed record class InfoboxFieldSchema
{
	/// <summary>Gets the key authors write before <c>=</c>, matched case-insensitively.</summary>
	public required string Key { get; init; }

	/// <summary>Gets whether the field may appear more than once, each value rendering as its own row.</summary>
	public bool Repeatable { get; init; }

	/// <summary>Gets editor-facing guidance on what the value should contain; not rendered on article pages.</summary>
	public string? Description { get; init; }

	/// <summary>Gets display labels keyed by culture code; an <c>en</c> label is required as the fallback.</summary>
	public required IReadOnlyDictionary<string, string> Labels { get; init; }
}
