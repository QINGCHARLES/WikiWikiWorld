namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// Finds the primary cover for article listings without fetching image files.
/// </summary>
public static class ArticleCoverReference
{
	/// <summary>
	/// Reads the first carousel cover, falling back to legacy infobox metadata for existing articles.
	/// </summary>
	/// <param name="Text">The article markdown.</param>
	/// <returns>The file article slug, or null when neither directive supplies a cover.</returns>
	public static string? Extract(string Text) =>
		ExtractFromDirective(Text, "{{CoverCarousel", true)
		?? ExtractFromDirective(Text, "{{PublicationIssueInfobox", false);

	/// <summary>
	/// Reads structured cover properties, respecting carousel item boundaries and duplicate-key behavior.
	/// </summary>
	/// <param name="Text">The article markdown.</param>
	/// <param name="Marker">The opening directive marker.</param>
	/// <param name="IsCarousel">Whether the last nonempty cover property within each item wins.</param>
	/// <returns>The first usable cover slug without its optional file prefix, or null.</returns>
	private static string? ExtractFromDirective(string Text, string Marker, bool IsCarousel)
	{
		int Start = Text.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
		if (Start < 0) return null;
		Start += Marker.Length;
		int End = Text.IndexOf("}}", Start, StringComparison.Ordinal);
		if (End < 0) return null;

		foreach (string Item in Text[Start..End].Split("|~|", StringSplitOptions.RemoveEmptyEntries))
		{
			string? Slug = null;
			foreach (string Property in Item.Split("|#|", StringSplitOptions.RemoveEmptyEntries))
			{
				int EqualsIndex = Property.IndexOf('=');
				if (EqualsIndex < 0 || !Property[..EqualsIndex].Trim().Equals("CoverImage", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				string Value = Property[(EqualsIndex + 1)..].Trim();
				if (Value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
				{
					Value = Value["file:".Length..];
				}
				if (Value.Length == 0) continue;
				if (!IsCarousel) return Value;
				Slug = Value;
			}
			if (Slug is not null) return Slug;
		}
		return null;
	}
}
