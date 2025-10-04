using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.MarkdigExtensions;

/// <summary>
/// Represents the footnote inline element in the markdown AST
/// </summary>
public sealed class FootnoteInline : LeafInline
{
	public required StringSlice Data { get; init; }
	public required int FootnoteNumber { get; init; }
}

/// <summary>
/// Main extension class that registers the parser and renderer
/// </summary>
public sealed class FootnoteExtension(List<Footnote> Footnotes) : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.InlineParsers.Contains<FootnoteParser>())
		{
			Pipeline.InlineParsers.Add(new FootnoteParser(Footnotes));
		}
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRendererInstance &&
			!HtmlRendererInstance.ObjectRenderers.Contains<FootnoteRenderer>())
		{
			HtmlRendererInstance.ObjectRenderers.Add(new FootnoteRenderer(Footnotes));
		}
	}
}

/// <summary>
/// Parser for footnote syntax {{Footnote texthere}}
/// </summary>
public sealed class FootnoteParser : InlineParser
{
	private const string MarkerStart = "{{Footnote ";
	private const string MarkerEnd = "}}";
	private readonly List<Footnote> Footnotes;

	public FootnoteParser(List<Footnote> Footnotes)
	{
		OpeningCharacters = ['{'];
		this.Footnotes = Footnotes;
	}

	public override bool Match(InlineProcessor Processor, ref StringSlice Slice)
	{
		int StartPosition = Slice.Start;

		// Check if the slice starts with the marker
		if (!Slice.Match(MarkerStart))
		{
			return false;
		}

		// Move past the marker
		Slice.Start += MarkerStart.Length;

		// Find the end marker
		int EndPos = Slice.Text.IndexOf(MarkerEnd, Slice.Start, StringComparison.Ordinal);
		if (EndPos == -1)
		{
			// No end marker found, reset and return false
			Slice.Start = StartPosition;
			return false;
		}

		// Extract the footnote text
		string FootnoteText = Slice.Text.Substring(Slice.Start, EndPos - Slice.Start).Trim();

		// Create the inline
		int InlineStart = Processor.GetSourcePosition(Slice.Start, out int Line, out int Column);
		Processor.Inline = new FootnoteInline
		{
			Span =
			{
				Start = InlineStart,
				End = InlineStart + (EndPos - Slice.Start)
			},
			Line = Line,
			Column = Column,
			Data = new StringSlice(Slice.Text, Slice.Start, EndPos - 1),
			FootnoteNumber = 0 // Will be set in the renderer
		};

		// Move past the end marker
		Slice.Start = EndPos + MarkerEnd.Length;
		return true;
	}


}

/// <summary>
/// Renderer that outputs a superscript number for the footnote
/// </summary>
public sealed class FootnoteRenderer : HtmlObjectRenderer<FootnoteInline>
{
	private readonly List<Footnote> Footnotes;

	public FootnoteRenderer(List<Footnote> Footnotes)
	{
		this.Footnotes = Footnotes;
	}

	protected override void Write(HtmlRenderer Renderer, FootnoteInline Inline)
	{
		// Add the footnote to the list and get its number
		int FootnoteNumber = AddFootnote(Inline.Data.ToString());

		// Write a superscript with the footnote number and make it a link to the footnote
		Renderer.Write("<sup id=\"fnref:");
		Renderer.Write(FootnoteNumber.ToString());
		Renderer.Write("\"><a href=\"#fn:");
		Renderer.Write(FootnoteNumber.ToString());
		Renderer.Write("\">");
		Renderer.Write(FootnoteNumber.ToString());
		Renderer.Write("</a></sup>");
	}

	/// <summary>
	/// Adds a footnote to the list and returns its number
	/// </summary>
	private int AddFootnote(string Text)
	{
		// Store the raw markdown text to be processed later
		int Number = Footnotes.Count + 1;
		Footnotes.Add(new Footnote
		{
			Number = Number,
			Text = Text
		});
		return Number;
	}
}

/// <summary>
/// Extension method to easily add the footnote extension to a Markdig pipeline
/// </summary>
public static class FootnoteExtensionMethod
{
	public static MarkdownPipelineBuilder UseFootnotes(this MarkdownPipelineBuilder Pipeline, List<Footnote> Footnotes)
	{
		Pipeline.Extensions.AddIfNotAlready(new FootnoteExtension(Footnotes));
		return Pipeline;
	}
}