using Markdig;
using Markdig.Syntax;
using System.Text.RegularExpressions;
using WikiWikiWorld.Web.MarkdigExtensions;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Finds problems an author should fix in an article that has just been saved. The save always succeeds; the API
/// returns these messages in <c>Warnings</c> and the web editor shows them once on the saved article.
/// </summary>
/// <remarks>
/// Besides the infobox checks in <see cref="InfoboxExtension.FindProblems"/>, it reports email addresses (which
/// articles include only when the address itself is notable), YouTube addresses and raw channel IDs written into the
/// text instead of <c>{{YouTubeLink}}</c>, and <c>{{YouTubeLink}}</c> directives that do not validate. Fenced code,
/// code spans and the payloads of <c>{{Citation}}</c>, <c>{{Source}}</c> and <c>{{YouTube}}</c>, where addresses
/// belong, are not checked.
/// </remarks>
public static partial class ArticleSaveWarnings
{
	/// <summary>Directives whose payloads may legitimately hold web addresses.</summary>
	private static readonly string[] AddressDirectiveMarkers = ["{{Citation ", "{{Source ", "{{YouTube "];

	/// <summary>
	/// Lists every save warning for an article's text.
	/// </summary>
	/// <param name="Text">The saved Markdown.</param>
	/// <param name="Pipeline">The site's article pipeline, used to parse infoboxes as they render.</param>
	/// <param name="Catalog">The infobox schemas loaded at startup.</param>
	/// <param name="SiteId">The site the article belongs to.</param>
	/// <returns>One plain-English message per problem, infobox problems first; empty when there are none or the
	/// text is blank.</returns>
	public static IReadOnlyList<string> Find(string? Text, MarkdownPipeline Pipeline, InfoboxSchemaCatalog Catalog, int SiteId)
	{
		ArgumentNullException.ThrowIfNull(Pipeline);
		ArgumentNullException.ThrowIfNull(Catalog);
		if (string.IsNullOrWhiteSpace(Text)) return [];

		MarkdownDocument Document = Markdown.Parse(Text, Pipeline);
		List<string> Warnings = [.. InfoboxExtension.FindProblems(Document, Catalog, SiteId)];

		// Masking with spaces keeps every remaining character at its original position, so line numbers stay right.
		char[] Prose = Text.ToCharArray();
		foreach (Match Code in FencedCodeRegex().Matches(Text)) Mask(Prose, Code.Index, Code.Length);
		foreach (Match Code in CodeSpanRegex().Matches(new string(Prose))) Mask(Prose, Code.Index, Code.Length);

		string Current = new(Prose);
		foreach (string Marker in AddressDirectiveMarkers) MaskDirectives(Prose, Current, Marker);

		Current = new string(Prose);
		for (int Start = Current.IndexOf(YouTubeLinkParser.MarkerStart, StringComparison.Ordinal); Start >= 0;
			Start = Current.IndexOf(YouTubeLinkParser.MarkerStart, Start + 1, StringComparison.Ordinal))
		{
			int PayloadStart = Start + YouTubeLinkParser.MarkerStart.Length;
			int End = DirectiveSyntax.FindClosingDelimiter(Current, PayloadStart);
			if (End < 0)
			{
				Warnings.Add($"Line {LineOf(Text, Start)}: {{{{YouTubeLink is not closed with }}}}, so it shows as text.");
				continue;
			}
			if (!YouTubeLinkParser.TryParsePayload(Current[PayloadStart..End], out _, out string? Problem))
			{
				Warnings.Add($"Line {LineOf(Text, Start)}: this {{{{YouTubeLink}}}} shows as text. {Problem}");
			}
			Mask(Prose, Start, End + 2 - Start);
		}

		Current = new string(Prose);
		foreach (Match Address in YouTubeAddressRegex().Matches(Current))
		{
			Warnings.Add($"Line {LineOf(Text, Address.Index)}: {Address.Value} is a YouTube address in the text. "
				+ "Name the video or channel with {{YouTubeLink Video=…, Channel=… or Handle=… |#| Label=…}}, "
				+ "or cite it with {{Citation … |#| url=…}}.");
			Mask(Prose, Address.Index, Address.Length);
		}

		Current = new string(Prose);
		foreach (Match ChannelId in ChannelIdRegex().Matches(Current))
		{
			Warnings.Add($"Line {LineOf(Text, ChannelId.Index)}: {ChannelId.Value} looks like a raw YouTube channel ID. "
				+ $"Write {{{{YouTubeLink Channel={ChannelId.Value} |#| Label=…}}}} with the channel's name instead.");
		}

		foreach (Match Email in EmailRegex().Matches(Current))
		{
			Warnings.Add($"Line {LineOf(Text, Email.Index)}: {Email.Value} is an email address. Remove it unless the "
				+ "address itself is notable, such as one printed as part of the publication's identity.");
		}

		return Warnings;
	}

	/// <summary>
	/// Blanks out each directive that starts with <paramref name="Marker"/>, through its matching closing braces.
	/// </summary>
	/// <param name="Prose">The characters being masked, changed in place.</param>
	/// <param name="Source">The same text as a string, for searching.</param>
	/// <param name="Marker">The directive's opening marker, such as <c>{{Citation </c>.</param>
	private static void MaskDirectives(char[] Prose, string Source, string Marker)
	{
		for (int Start = Source.IndexOf(Marker, StringComparison.Ordinal); Start >= 0;
			Start = Source.IndexOf(Marker, Start + 1, StringComparison.Ordinal))
		{
			int End = DirectiveSyntax.FindClosingDelimiter(Source, Start + Marker.Length);
			if (End >= 0) Mask(Prose, Start, End + 2 - Start);
		}
	}

	/// <summary>
	/// Replaces a range with spaces, keeping line breaks so later line numbers do not move.
	/// </summary>
	/// <param name="Prose">The characters being masked, changed in place.</param>
	/// <param name="Start">The first position to blank.</param>
	/// <param name="Length">How many characters to blank.</param>
	private static void Mask(char[] Prose, int Start, int Length)
	{
		for (int i = Start; i < Start + Length && i < Prose.Length; i++)
		{
			if (Prose[i] is not ('\n' or '\r')) Prose[i] = ' ';
		}
	}

	/// <summary>
	/// Gives the one-based line number of a position in the text.
	/// </summary>
	/// <param name="Text">The saved Markdown.</param>
	/// <param name="Position">A zero-based character position.</param>
	/// <returns>The line the position is on, counting from 1.</returns>
	private static int LineOf(string Text, int Position) => Text.AsSpan(0, Position).Count('\n') + 1;

	/// <summary>Fenced code blocks opened by three or more backticks or tildes, through the matching fence.</summary>
	[GeneratedRegex(@"^[ ]{0,3}(`{3,}|~{3,})[^\n]*\n.*?^[ ]{0,3}\1[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline)]
	private static partial Regex FencedCodeRegex();

	/// <summary>Inline code spans: a backtick run, its content, and a closing run of the same length.</summary>
	[GeneratedRegex(@"(?<!`)(`+)(?!`).+?(?<!`)\1(?!`)", RegexOptions.Singleline)]
	private static partial Regex CodeSpanRegex();

	/// <summary>Web addresses on youtube.com, m.youtube.com or youtu.be, with or without a scheme.</summary>
	[GeneratedRegex(@"(?:https?://)?(?:www\.|m\.)?(?:youtube\.com|youtu\.be)/[^\s)\]|>""]+", RegexOptions.IgnoreCase)]
	private static partial Regex YouTubeAddressRegex();

	/// <summary>A YouTube channel ID standing on its own: UC followed by 22 URL-safe base64 characters.</summary>
	[GeneratedRegex(@"(?<![\w-])UC[A-Za-z0-9_-]{22}(?![\w-])")]
	private static partial Regex ChannelIdRegex();

	/// <summary>An email address; the character before it may not be part of an address or a URL path.</summary>
	[GeneratedRegex(@"(?<![\w.%+/@-])[\w.%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")]
	private static partial Regex EmailRegex();
}
