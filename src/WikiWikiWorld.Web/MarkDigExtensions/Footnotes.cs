using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using System.Text.RegularExpressions;

namespace WikiWikiWorld.MarkdigExtensions;

/// <summary>
/// Extension that renders the collected footnotes at the {{Footnotes}} marker
/// </summary>
public sealed class FootnotesExtension(List<Footnote> Footnotes) : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<FootnotesParser>())
		{
			Pipeline.BlockParsers.Add(new FootnotesParser());
		}
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRendererInstance &&
			!HtmlRendererInstance.ObjectRenderers.Contains<FootnotesRenderer>())
		{
			HtmlRendererInstance.ObjectRenderers.Add(new FootnotesRenderer(Footnotes));
		}
	}
}

/// <summary>
/// Parser that identifies the {{Footnotes}} marker
/// </summary>
public sealed class FootnotesParser : BlockParser
{
	private const string Marker = "{{Footnotes}}";

	public FootnotesParser()
	{
		OpeningCharacters = ['{'];
	}

	public override BlockState TryOpen(BlockProcessor Processor)
	{
		// Check if the line starts with "{{Footnotes}}"
		if (!Processor.Line.Match(Marker))
		{
			return BlockState.None;
		}

		// Move cursor forward to avoid infinite loop
		Processor.Line.Start += Marker.Length;

		// Push new block
		Processor.NewBlocks.Push(new FootnotesBlock(this));
		return BlockState.BreakDiscard;
	}
}

/// <summary>
/// Block representing the {{Footnotes}} marker
/// </summary>
public sealed class FootnotesBlock : LeafBlock
{
	public FootnotesBlock(BlockParser Parser) : base(Parser) { }
}

/// <summary>
/// Renderer that outputs the list of footnotes in HTML
/// </summary>
public sealed class FootnotesRenderer(List<Footnote> Footnotes) : HtmlObjectRenderer<FootnotesBlock>
{
	protected override void Write(HtmlRenderer Renderer, FootnotesBlock Block)
	{
		if (Footnotes is not null && Footnotes.Count > 0)
		{
			// Start footnotes section
			Renderer.Write("<div class=\"footnotes\">");
			Renderer.Write("<hr />");
			Renderer.Write("<ol>");

			// Write each footnote
			foreach (Footnote Footnote in Footnotes)
			{
				Renderer.Write("<li id=\"fn:");
				Renderer.Write(Footnote.Number.ToString());
				Renderer.Write("\">");

				// Process the footnote text as markdown
				string ProcessedText = ProcessFootnoteMarkdown(Footnote.Text);
				Renderer.WriteLine(ProcessedText);

				// Add back-reference link
				Renderer.Write(" <a href=\"#fnref:");
				Renderer.Write(Footnote.Number.ToString());
				Renderer.Write("\" class=\"footnote-backref\">↩</a>");

				Renderer.Write("</li>");
			}

			// End footnotes section
			Renderer.Write("</ol>");
			Renderer.Write("</div>");
		}
	}

	/// <summary>
	/// Processes the footnote text as markdown
	/// </summary>
	private string ProcessFootnoteMarkdown(string MarkdownText)
	{
		// Create a temporary inline processor to handle the markdown content
		// This prevents infinite recursion if there are footnotes inside footnotes
		MarkdownPipeline TempPipeline = new MarkdownPipelineBuilder()
			.UseAdvancedExtensions()
			.Build();

		// Process the markdown text to HTML
		string ProcessedHtml = Markdown.ToHtml(MarkdownText, TempPipeline);

		// Remove surrounding paragraph tags if they exist
		if (ProcessedHtml.StartsWith("<p>") && ProcessedHtml.EndsWith("</p>\n"))
		{
			ProcessedHtml = ProcessedHtml.Substring(3, ProcessedHtml.Length - 8);
		}

		return ProcessedHtml;
	}
}

/// <summary>
/// Extension method to easily add the footnotes extension to a Markdig pipeline
/// </summary>
public static class FootnotesExtensionMethod
{
	public static MarkdownPipelineBuilder UseFootnotes(this MarkdownPipelineBuilder Pipeline, List<Footnote> Footnotes)
	{
		Pipeline.Extensions.AddIfNotAlready(new FootnotesExtension(Footnotes));
		return Pipeline;
	}
}