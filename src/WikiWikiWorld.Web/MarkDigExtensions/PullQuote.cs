using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A Markdown inline element representing a pull quote.
/// </summary>
public class PullQuoteInline : LeafInline
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
public class PullQuoteInlineParser : InlineParser
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
		if (!Slice.Match(MarkerStart))
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

		string Content = Slice.Text.Substring(Slice.Start, EndPos - Slice.Start).Trim();

		// Parse quote and attribution
		string QuoteText;
		string? Attribution = null;

		int AttributeStart = Content.IndexOf(AttributeSeparator, StringComparison.Ordinal);
		if (AttributeStart != -1)
		{
			QuoteText = Content.Substring(0, AttributeStart).Trim();
			Attribution = Content.Substring(AttributeStart + AttributeSeparator.Length).Trim();
		}
		else
		{
			QuoteText = Content.Trim();
		}

		// If the quote is wrapped in quotes, remove them
		if (QuoteText.StartsWith("\"") && QuoteText.EndsWith("\""))
		{
			QuoteText = QuoteText.Substring(1, QuoteText.Length - 2);
		}

		// Create the inline element
		PullQuoteInline InlineElement = new()
		{
			QuoteText = QuoteText,
			Attribution = Attribution
		};

		Processor.Inline = InlineElement;
		Slice.Start = EndPos + MarkerEnd.Length;
		return true;
	}
}

/// <summary>
/// Renders a <see cref="PullQuoteInline"/> to HTML.
/// </summary>
public class PullQuoteInlineRenderer : HtmlObjectRenderer<PullQuoteInline>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, PullQuoteInline InlineElement)
	{
		// The zig-zag borders are now applied via CSS ::before and ::after pseudo-elements
		Renderer.Write("<blockquote class=\"pullquote\">");
		Renderer.Write($"<p>{InlineElement.QuoteText}</p>");

		if (!string.IsNullOrEmpty(InlineElement.Attribution))
		{
			Renderer.Write($"<cite>{InlineElement.Attribution}</cite>");
		}

		Renderer.Write("</blockquote>");
	}
}

/// <summary>
/// A Markdig extension that adds support for pull quotes.
/// </summary>
public class PullQuoteExtension : IMarkdownExtension
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
}