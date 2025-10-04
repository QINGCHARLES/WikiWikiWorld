using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace WikiWikiWorld.Web.MarkdigExtensions;

public class PullQuoteInline : LeafInline
{
	public required string QuoteText { get; init; }
	public string? Attribution { get; init; }
}

public class PullQuoteInlineParser : InlineParser
{
	private const string MarkerStart = "{{PullQuote ";
	private const string MarkerEnd = "}}";
	private const string AttributeSeparator = "|#|";

	public PullQuoteInlineParser()
	{
		OpeningCharacters = ['{'];
	}

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

public class PullQuoteInlineRenderer : HtmlObjectRenderer<PullQuoteInline>
{
	protected override void Write(HtmlRenderer Renderer, PullQuoteInline InlineElement)
	{
		// Generate HTML for the pull quote
		Renderer.Write("<div class=\"zig-zag-line\"></div>");

		Renderer.Write("<blockquote class=\"pullquote\">");
		Renderer.Write($"<p>{InlineElement.QuoteText}</p>");

		if (!string.IsNullOrEmpty(InlineElement.Attribution))
		{
			Renderer.Write($"<cite>{InlineElement.Attribution}</cite>");
		}

		Renderer.Write("</blockquote>");

		Renderer.Write("<div class=\"zig-zag-line\"></div>");
	}
}

public class PullQuoteExtension : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.InlineParsers.Contains<PullQuoteInlineParser>())
		{
			Pipeline.InlineParsers.Add(new PullQuoteInlineParser());
		}
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer &&
			!HtmlRenderer.ObjectRenderers.Any(r => r is PullQuoteInlineRenderer))
		{
			HtmlRenderer.ObjectRenderers.Add(new PullQuoteInlineRenderer());
		}
	}
}