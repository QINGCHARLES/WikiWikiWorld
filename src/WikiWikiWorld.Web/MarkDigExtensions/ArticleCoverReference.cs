namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// Finds the primary cover for article listings without fetching image files.
/// </summary>
public static class ArticleCoverReference
{
	private const string MarkerStart = "{{CoverCarousel";

	/// <summary>
	/// Reads the primary cover slug from the first <c>{{CoverCarousel}}</c> directive in the article.
	/// </summary>
	/// <param name="Text">The article markdown.</param>
	/// <returns>The file article slug without its optional <c>file:</c> prefix, or <see langword="null"/> when no cover is present.</returns>
	public static string? Extract(string Text)
	{
		int Start = Text.IndexOf(MarkerStart, StringComparison.OrdinalIgnoreCase);
		if (Start < 0) return null;
		Start += MarkerStart.Length;
		if (Start < Text.Length && !char.IsWhiteSpace(Text[Start]) &&
			!Text.AsSpan(Start).StartsWith("}}", StringComparison.Ordinal))
		{
			return null;
		}

		int End = DirectiveSyntax.FindClosingDelimiter(Text, Start);
		if (End < 0) return null;

		string Payload = Text[Start..End];
		string[] Items = Payload.Split("|~|", StringSplitOptions.RemoveEmptyEntries);
		foreach (string Item in Items)
		{
			string? Slug = null;
			foreach (string Property in DirectiveSyntax.SplitProperties(Item))
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
				Slug = Value;
			}
			if (Slug is not null) return Slug;
		}
		return null;
	}
}
