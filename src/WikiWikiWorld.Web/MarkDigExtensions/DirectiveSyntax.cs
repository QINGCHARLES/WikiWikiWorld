namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>Reads directive boundaries without treating nested properties or inline code as outer syntax.</summary>
internal static class DirectiveSyntax
{
	/// <summary>Finds the matching end after a directive's opening marker has been consumed.</summary>
	/// <param name="Text">The source containing the directive payload.</param>
	/// <param name="Start">The first character after its opening marker.</param>
	/// <returns>The matching closing-brace position, or -1 for incomplete input.</returns>
	internal static int FindClosingDelimiter(string Text, int Start)
	{
		int Depth = 0;
		foreach ((int Position, string Token) in ReadTokens(Text, Start))
		{
			if (Token == "{{") Depth++;
			else if (Token == "}}")
			{
				if (Depth == 0) return Position;
				Depth--;
			}
		}
		return -1;
	}

	/// <summary>Splits outer properties, retaining nested separators and Markdown escapes in each value.</summary>
	/// <param name="Text">The payload without its enclosing directive delimiters.</param>
	/// <returns>Property strings in source order, including empty properties.</returns>
	internal static IEnumerable<string> SplitProperties(string Text)
	{
		int Depth = 0;
		int Start = 0;
		foreach ((int Position, string Token) in ReadTokens(Text, 0))
		{
			if (Token == "{{") Depth++;
			else if (Token == "}}") Depth--;
			else if (Depth == 0)
			{
				yield return Text[Start..Position];
				Start = Position + Token.Length;
			}
		}
		yield return Text[Start..];
	}

	/// <summary>Enumerates structural tokens outside escaped punctuation and matched Markdown code spans.</summary>
	/// <param name="Text">The complete source being scanned.</param>
	/// <param name="Start">The first source position to inspect.</param>
	/// <returns>The positions and text of unescaped structural tokens outside code spans.</returns>
	private static IEnumerable<(int Position, string Token)> ReadTokens(string Text, int Start)
	{
		for (int i = Start; i < Text.Length; i++)
		{
			if (Text[i] == '\\')
			{
				i++;
				continue;
			}
			if (Text[i] == '`')
			{
				i = FindCodeSpanEnd(Text, i);
				continue;
			}
			string? Token = Text.AsSpan(i).StartsWith("{{", StringComparison.Ordinal) ? "{{"
				: Text.AsSpan(i).StartsWith("}}", StringComparison.Ordinal) ? "}}"
				: Text.AsSpan(i).StartsWith("|#|", StringComparison.Ordinal) ? "|#|" : null;
			if (Token is null) continue;
			yield return (i, Token);
			i += Token.Length - 1;
		}
	}

	/// <summary>Skips code between equal backtick runs; unmatched opening backticks remain literal.</summary>
	/// <param name="Text">The source containing an opening backtick run.</param>
	/// <param name="Start">The first backtick in that run.</param>
	/// <returns>The final closing backtick, or the end of an unmatched opening run.</returns>
	private static int FindCodeSpanEnd(string Text, int Start)
	{
		int OpeningEnd = Start;
		while (OpeningEnd < Text.Length && Text[OpeningEnd] == '`') OpeningEnd++;
		int OpeningLength = OpeningEnd - Start;
		for (int i = OpeningEnd; i < Text.Length; i++)
		{
			if (Text[i] != '`') continue;
			int ClosingStart = i;
			while (i < Text.Length && Text[i] == '`') i++;
			if (i - ClosingStart == OpeningLength) return i - 1;
			i--;
		}
		return OpeningEnd - 1;
	}
}
