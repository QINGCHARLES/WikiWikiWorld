using System.Net;
using System.Text.RegularExpressions;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// Formats short directive values, such as pull-quote text or timeline event titles, that allow emphasis but no
/// other Markdown.
/// </summary>
internal static partial class InlineEmphasis
{
	/// <summary>
	/// HTML-encodes text, then converts Markdown bold (<c>**text**</c>) and italic (<c>*text*</c>) spans.
	/// Links, code spans and other Markdown stay literal.
	/// </summary>
	/// <param name="Text">The raw text to format.</param>
	/// <returns>Safe HTML in which only <c>&lt;strong&gt;</c> and <c>&lt;em&gt;</c> tags are markup.</returns>
	internal static string ToHtml(string Text)
	{
		string Encoded = WebUtility.HtmlEncode(Text);
		Encoded = BoldMarkupRegex().Replace(Encoded, "<strong>$1</strong>");
		Encoded = ItalicMarkupRegex().Replace(Encoded, "<em>$1</em>");
		return Encoded;
	}

	/// <summary>
	/// Matches a <c>**bold**</c> span; group 1 is the inner text.
	/// </summary>
	[GeneratedRegex(@"\*\*(.+?)\*\*")]
	private static partial Regex BoldMarkupRegex();

	/// <summary>
	/// Matches a single-asterisk <c>*italic*</c> span that is not part of a <c>**bold**</c> marker; group 1 is the inner text.
	/// </summary>
	[GeneratedRegex(@"(?<!\*)\*([^*]+?)\*(?!\*)")]
	private static partial Regex ItalicMarkupRegex();
}
