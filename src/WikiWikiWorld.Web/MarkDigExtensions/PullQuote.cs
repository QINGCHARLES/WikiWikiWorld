using System.Text.RegularExpressions;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A Markdown inline element representing a pull quote.
/// </summary>
public sealed class PullQuoteInline : LeafInline
{
	/// <summary>
	/// Gets or initializes the text of the quote.
	/// </summary>
	public required string QuoteText { get; init; }

	/// <summary>
	/// Gets or initializes the attribution for the quote.
	/// </summary>
	public string? Attribution { get; init; }
}

/// <summary>
/// Parses the {{PullQuote ...}} inline syntax.
/// </summary>
public sealed partial class PullQuoteInlineParser : InlineParser
{
	private const string MarkerStart = "{{PullQuote ";
	private const string MarkerEnd = "}}";
	private const string AttributeSeparator = "|#|";

	/// <summary>
	/// Initializes a new instance of the <see cref="PullQuoteInlineParser"/> class.
	/// </summary>
	public PullQuoteInlineParser()
	{
		OpeningCharacters = ['{'];
	}

	/// <inheritdoc/>
	public override bool Match(InlineProcessor Processor, ref StringSlice Slice)
	{
		int StartPosition = Slice.Start;
		if (!Slice.MatchLowercase(MarkerStart.ToLowerInvariant()))
		{
			return false;
		}

		Slice.Start += MarkerStart.Length;
		int EndPos = Slice.Text.IndexOf(MarkerEnd, Slice.Start, StringComparison.Ordinal);
		if (EndPos == -1)
		{
			Slice.Start = StartPosition;
			return false;
		}

		string Content = Slice.Text[Slice.Start..EndPos].Trim();

		string QuoteText;
		string? Attribution = null;

		int AttributeStart = Content.IndexOf(AttributeSeparator, StringComparison.Ordinal);
		if (AttributeStart != -1)
		{
			QuoteText = Content[..AttributeStart].Trim();
			Attribution = Content[(AttributeStart + AttributeSeparator.Length)..].Trim();
		}
		else
		{
			Match DashAttributionMatch = TrailingDashAttributionRegex().Match(Content);
			if (DashAttributionMatch.Success)
			{
				QuoteText = DashAttributionMatch.Groups[1].Value.Trim();
				Attribution = DashAttributionMatch.Groups[2].Value.Trim();
			}
			else
			{
				QuoteText = Content;
			}
		}

		QuoteText = TrimOuterQuotes(QuoteText);
		if (!string.IsNullOrWhiteSpace(Attribution))
		{
			Attribution = Attribution.TrimStart('—', '–', '-', ' ').Trim();
		}

		PullQuoteInline InlineElement = new()
		{
			QuoteText = QuoteText,
			Attribution = Attribution
		};

		Processor.Inline = InlineElement;
		Slice.Start = EndPos + MarkerEnd.Length;
		return true;
	}

	/// <summary>
	/// Strips a matching pair of outer straight or curly double quotes from the quote text.
	/// </summary>
	/// <param name="Text">The raw quote text.</param>
	/// <returns>The unquoted text.</returns>
	private static string TrimOuterQuotes(string Text)
	{
		string Trimmed = Text.Trim();
		if (Trimmed.Length >= 2)
		{
			if ((Trimmed.StartsWith('"') && Trimmed.EndsWith('"')) ||
				(Trimmed.StartsWith('“') && Trimmed.EndsWith('”')) ||
				(Trimmed.StartsWith('“') && Trimmed.EndsWith('"')) ||
				(Trimmed.StartsWith('"') && Trimmed.EndsWith('”')))
			{
				return Trimmed[1..^1].Trim();
			}
		}

		return Trimmed;
	}

	/// <summary>
	/// Matches a quote followed by a dash-introduced attribution, either on its own line
	/// (<c>quote⏎— Name</c>) or directly after a closing double quote (<c>"quote" — Name</c>).
	/// Group 1 keeps any closing quote mark so <see cref="TrimOuterQuotes"/> can strip the pair.
	/// </summary>
	[GeneratedRegex(@"^(.*?\S)(?:\s*\r?\n\s*[—–-]\s*|(?<=[""”])\s*[—–-]\s+)(.+)$", RegexOptions.Singleline)]
	private static partial Regex TrailingDashAttributionRegex();
}

/// <summary>
/// Renders a <see cref="PullQuoteInline"/> to HTML.
/// </summary>
public sealed class PullQuoteInlineRenderer : HtmlObjectRenderer<PullQuoteInline>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, PullQuoteInline InlineElement)
	{
		Renderer.Write("<blockquote class=\"pullquote\">");
		Renderer.Write($"<p>\u201c{InlineEmphasis.ToHtml(InlineElement.QuoteText)}\u201d</p>");

		if (!string.IsNullOrWhiteSpace(InlineElement.Attribution))
		{
			Renderer.Write($"<cite>{InlineEmphasis.ToHtml(InlineElement.Attribution)}</cite>");
		}

		Renderer.Write("</blockquote>");
	}
}

/// <summary>
/// A Markdig extension that adds support for pull quotes.
/// </summary>
public sealed class PullQuoteExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.InlineParsers.Contains<PullQuoteInlineParser>())
		{
			Pipeline.InlineParsers.Add(new PullQuoteInlineParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer &&
			!HtmlRenderer.ObjectRenderers.Any(r => r is PullQuoteInlineRenderer))
		{
			HtmlRenderer.ObjectRenderers.Add(new PullQuoteInlineRenderer());
		}
	}

	/// <summary>The fewest body paragraphs allowed between two pull quotes.</summary>
	public const int MinParagraphsBetween = 4;

	/// <summary>
	/// Finds pull quotes that sit too close to the previous one. Two pull quotes on one screen read as a stack of boxes,
	/// so the Magazedia prompts ask for at least <see cref="MinParagraphsBetween"/> body paragraphs between them.
	/// </summary>
	/// <param name="Document">The parsed article, with inlines.</param>
	/// <returns>One message per crowded pull quote, naming its line; empty when there are none.</returns>
	/// <remarks>Only top-level paragraphs that hold prose count. Headings, lists, tables and paragraphs made only of
	/// directives (infoboxes, images, other pull quotes) do not.</remarks>
	public static IReadOnlyList<string> FindProblems(MarkdownDocument Document)
	{
		ArgumentNullException.ThrowIfNull(Document);

		List<string> Problems = [];
		int? ParagraphsSinceQuote = null;
		foreach (Block Block in Document)
		{
			if (Block is not ParagraphBlock { Inline: { } Inlines } Paragraph) continue;
			if (Inlines.Descendants<PullQuoteInline>().Any())
			{
				if (ParagraphsSinceQuote < MinParagraphsBetween)
				{
					Problems.Add($"Line {Paragraph.Line + 1}: this {{{{PullQuote}}}} has only {ParagraphsSinceQuote} body "
						+ $"paragraph(s) after the previous one. Keep at least {MinParagraphsBetween} paragraphs between pull "
						+ "quotes, each in the section that cites it, or use just one.");
				}
				ParagraphsSinceQuote = 0;
			}
			else if (ParagraphsSinceQuote is not null && HasProse(Inlines))
			{
				ParagraphsSinceQuote++;
			}
		}
		return Problems;
	}

	/// <summary>
	/// Tells a prose paragraph from one made only of directives, which parse as leaf inlines with no literal text.
	/// </summary>
	/// <param name="Inlines">The paragraph's inline content.</param>
	/// <returns><see langword="true"/> when the paragraph contains visible literal text.</returns>
	private static bool HasProse(ContainerInline Inlines) =>
		Inlines.Descendants<LiteralInline>().Any(Literal => !Literal.Content.IsEmptyOrWhitespace());
}