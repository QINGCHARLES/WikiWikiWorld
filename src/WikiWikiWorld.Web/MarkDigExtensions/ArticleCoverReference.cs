namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// Represents a parsed <c>{{HeaderImage}}</c> reference extracted from article Markdown without full AST rendering.
/// </summary>
/// <param name="Slug">The file article slug without any <c>file:</c> prefix.</param>
/// <param name="Focus">The normalized vertical focus (<c>"top"</c>, <c>"center"</c>, or <c>"bottom"</c>), or <see langword="null"/> when omitted or unrecognized.</param>
public sealed record HeaderImageReference(string Slug, string? Focus);

/// <summary>
/// Finds primary cover and fallback header image references for article listings without fetching image files.
/// </summary>
public static class ArticleCoverReference
{
	private const string CoverMarkerStart = "{{CoverCarousel";
	private const string HeaderMarkerStart = "{{HeaderImage ";
	private const string MarkerEnd = "}}";

	/// <summary>
	/// Reads the primary cover slug from the first <c>{{CoverCarousel}}</c> directive in the article.
	/// </summary>
	/// <param name="Text">The article markdown.</param>
	/// <returns>The file article slug without its optional <c>file:</c> prefix, or <see langword="null"/> when no cover is present.</returns>
	public static string? Extract(string Text)
	{
		int Start = Text.IndexOf(CoverMarkerStart, StringComparison.OrdinalIgnoreCase);
		if (Start < 0) return null;
		Start += CoverMarkerStart.Length;
		if (Start < Text.Length && !char.IsWhiteSpace(Text[Start]) &&
			!Text.AsSpan(Start).StartsWith(MarkerEnd, StringComparison.Ordinal))
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

	/// <summary>
	/// Reads <c>{{HeaderImage}}</c> block directives in document order, matching <see cref="HeaderImageBlockParser"/>
	/// while skipping indented and fenced code blocks.
	/// </summary>
	/// <param name="Text">The article markdown.</param>
	/// <returns>Ordered header image references whose file slugs are non-empty; empty when none are present.</returns>
	public static IReadOnlyList<HeaderImageReference> ExtractHeaders(string Text)
	{
		if (!Text.Contains(HeaderMarkerStart, StringComparison.Ordinal))
		{
			return [];
		}

		List<HeaderImageReference> Results = [];
		char ActiveFenceChar = '\0';
		int ActiveFenceLength = 0;

		foreach (ReadOnlySpan<char> RawLine in Text.AsSpan().EnumerateLines())
		{
			int LeadingSpaces = 0;
			while (LeadingSpaces < RawLine.Length && RawLine[LeadingSpaces] == ' ')
			{
				LeadingSpaces++;
			}

			if (ActiveFenceLength > 0)
			{
				if (LeadingSpaces < 4 && IsClosingFence(RawLine[LeadingSpaces..], ActiveFenceChar, ActiveFenceLength))
				{
					ActiveFenceChar = '\0';
					ActiveFenceLength = 0;
				}
				continue;
			}

			if (LeadingSpaces >= 4 || (LeadingSpaces < RawLine.Length && RawLine[LeadingSpaces] == '\t'))
			{
				continue;
			}

			ReadOnlySpan<char> Line = RawLine[LeadingSpaces..];
			if (TryOpenFence(Line, out char FenceChar, out int FenceLength))
			{
				ActiveFenceChar = FenceChar;
				ActiveFenceLength = FenceLength;
				continue;
			}

			if (!Line.StartsWith(HeaderMarkerStart, StringComparison.Ordinal))
			{
				continue;
			}

			ReadOnlySpan<char> Remainder = Line[HeaderMarkerStart.Length..];
			int EndIndex = Remainder.IndexOf(MarkerEnd, StringComparison.Ordinal);
			if (EndIndex < 0)
			{
				continue;
			}

			string[] Segments = Remainder[..EndIndex].ToString().Split("|#|", StringSplitOptions.TrimEntries);
			if (Segments.Length == 0)
			{
				continue;
			}

			string RawSlug = Segments[0];
			if (RawSlug.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
			{
				RawSlug = RawSlug["file:".Length..].Trim();
			}

			if (RawSlug.Length == 0)
			{
				continue;
			}

			string? Focus = null;
			for (int i = 1; i < Segments.Length; i++)
			{
				string Segment = Segments[i];
				int EqualsIndex = Segment.IndexOf('=');
				if (EqualsIndex > 0 && Segment[..EqualsIndex].Trim().Equals("Focus", StringComparison.OrdinalIgnoreCase))
				{
					Focus = HeaderImageBlockParser.NormalizeFocus(Segment[(EqualsIndex + 1)..]);
				}
			}

			Results.Add(new HeaderImageReference(RawSlug, Focus));
		}

		return Results;
	}

	/// <summary>
	/// Checks whether a non-indented line opens a CommonMark fenced code block.
	/// </summary>
	/// <param name="Line">The line after up to three leading spaces.</param>
	/// <param name="FenceChar">Receives the fence character (<c>`</c> or <c>~</c>) when opened.</param>
	/// <param name="FenceLength">Receives the run length of the opening fence.</param>
	/// <returns><see langword="true"/> when the line opens a fenced code block; otherwise <see langword="false"/>.</returns>
	private static bool TryOpenFence(ReadOnlySpan<char> Line, out char FenceChar, out int FenceLength)
	{
		FenceChar = '\0';
		FenceLength = 0;
		if (Line.Length < 3 || (Line[0] != '`' && Line[0] != '~'))
		{
			return false;
		}

		char Candidate = Line[0];
		int Count = 0;
		while (Count < Line.Length && Line[Count] == Candidate)
		{
			Count++;
		}

		if (Count < 3)
		{
			return false;
		}

		// Backtick info strings cannot contain backticks on the opening line.
		if (Candidate == '`' && Line[Count..].Contains('`'))
		{
			return false;
		}

		FenceChar = Candidate;
		FenceLength = Count;
		return true;
	}

	/// <summary>
	/// Checks whether a line closes an open fenced code block.
	/// </summary>
	/// <param name="Line">The line after up to three leading spaces.</param>
	/// <param name="FenceChar">The active opening fence character.</param>
	/// <param name="MinLength">The minimum closing fence run length.</param>
	/// <returns><see langword="true"/> when the line closes the fence; otherwise <see langword="false"/>.</returns>
	private static bool IsClosingFence(ReadOnlySpan<char> Line, char FenceChar, int MinLength)
	{
		int Count = 0;
		while (Count < Line.Length && Line[Count] == FenceChar)
		{
			Count++;
		}

		return Count >= MinLength && Line[Count..].IsWhiteSpace();
	}
}
