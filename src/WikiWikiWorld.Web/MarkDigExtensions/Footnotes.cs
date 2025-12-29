using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace WikiWikiWorld.Web.MarkdigExtensions;

// --- Data Models (AST Nodes) ---

public sealed class FootnoteInline : LeafInline
{
	public required StringSlice Data { get; init; }
	public int FootnoteNumber { get; set; }
}

public sealed class Footnote(BlockParser? Parser) : ContainerBlock(Parser)
{
	public int Number { get; set; }
}

public sealed class FootnotesBlock(BlockParser Parser) : ContainerBlock(Parser)
{
}

// --- Parsers ---

public sealed class FootnoteParser : InlineParser
{
	private const string MarkerStart = "{{Footnote ";
	private const string MarkerEnd = "}}";

	public FootnoteParser() => OpeningCharacters = ['{'];

	public override bool Match(InlineProcessor Processor, ref StringSlice Slice)
	{
		int StartPosition = Slice.Start;
		if (!Slice.Match(MarkerStart)) return false;

		Slice.Start += MarkerStart.Length;
		int EndPos = Slice.Text.IndexOf(MarkerEnd, Slice.Start, StringComparison.Ordinal);
		
		if (EndPos == -1)
		{
			Slice.Start = StartPosition;
			return false;
		}

		int InlineStart = Processor.GetSourcePosition(Slice.Start, out int Line, out int Column);

		Processor.Inline = new FootnoteInline
		{
			Span = new SourceSpan(InlineStart, InlineStart + (EndPos - Slice.Start)),
			Line = Line,
			Column = Column,
			Data = new StringSlice(Slice.Text, Slice.Start, EndPos - 1),
			FootnoteNumber = 0 // Assigned in ReprocessFootnotes
		};

		Slice.Start = EndPos + MarkerEnd.Length;
		return true;
	}
}

public sealed class FootnotesBlockParser : BlockParser
{
	private const string Marker = "{{Footnotes}}";

	public FootnotesBlockParser() => OpeningCharacters = ['{'];

	public override BlockState TryOpen(BlockProcessor Processor)
	{
		if (!Processor.Line.Match(Marker)) return BlockState.None;

		Processor.Line.Start += Marker.Length;
		Processor.NewBlocks.Push(new FootnotesBlock(this));
		return BlockState.BreakDiscard;
	}
}

// --- Renderers ---

public sealed class FootnoteInlineRenderer : HtmlObjectRenderer<FootnoteInline>
{
	protected override void Write(HtmlRenderer Renderer, FootnoteInline Inline)
	{
		string Display = Inline.FootnoteNumber > 0 ? Inline.FootnoteNumber.ToString() : "?";
		Renderer.Write($"<sup id=\"fnref:{Display}\"><a href=\"#fn:{Display}\">{Display}</a></sup>");
	}
}

public sealed class FootnotesBlockRenderer : HtmlObjectRenderer<FootnotesBlock>
{
	protected override void Write(HtmlRenderer Renderer, FootnotesBlock Block)
	{
		if (Block.Count > 0)
		{
			Renderer.Write("<div class=\"footnotes\">");
			Renderer.Write("<hr />");
			Renderer.Write("<ol>");
			Renderer.WriteChildren(Block);
			Renderer.Write("</ol>");
			Renderer.Write("</div>");
		}
	}
}

	public sealed class FootnoteItemRenderer : HtmlObjectRenderer<Footnote>
{
	protected override void Write(HtmlRenderer Renderer, Footnote Block)
	{
		Renderer.Write($"<li id=\"fn:{Block.Number}\">");
		
		// If the footnote contains a single paragraph, render its content directly to avoid wrapping <p> tags
		if (Block.Count == 1 && Block[0] is ParagraphBlock Paragraph)
		{
			Renderer.WriteLeafInline(Paragraph);
		}
		else
		{
			// Otherwise render children normally
			Renderer.WriteChildren(Block);
		}
		
		// Add back-reference
		Renderer.Write($" <a href=\"#fnref:{Block.Number}\" class=\"footnote-backref\">↩</a>");
		Renderer.Write("</li>");
	}
}

// --- Extension Definition ---

public sealed class FootnoteExtension : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.InlineParsers.Contains<FootnoteParser>())
			Pipeline.InlineParsers.Add(new FootnoteParser());
		
		if (!Pipeline.BlockParsers.Contains<FootnotesBlockParser>())
			Pipeline.BlockParsers.Add(new FootnotesBlockParser());
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer)
		{
			if (!HtmlRenderer.ObjectRenderers.Contains<FootnoteInlineRenderer>())
				HtmlRenderer.ObjectRenderers.Add(new FootnoteInlineRenderer());
			
			if (!HtmlRenderer.ObjectRenderers.Contains<FootnotesBlockRenderer>())
				HtmlRenderer.ObjectRenderers.Add(new FootnotesBlockRenderer());

			if (!HtmlRenderer.ObjectRenderers.Contains<FootnoteItemRenderer>())
				HtmlRenderer.ObjectRenderers.Add(new FootnoteItemRenderer());
		}
	}

	/// <summary>
	/// Reprocesses document to number footnotes and parses their content using the Singleton Pipeline.
	/// </summary>
	public static void ReprocessFootnotes(MarkdownDocument Document, MarkdownPipeline Pipeline)
	{
		List<FootnoteInline> Inlines = [.. Document.Descendants<FootnoteInline>()];
		if (Inlines.Count == 0) return;

		FootnotesBlock? TargetBlock = Document.Descendants<FootnotesBlock>().FirstOrDefault();
		int Counter = 1;

		foreach (FootnoteInline Inline in Inlines)
		{
			Inline.FootnoteNumber = Counter;

			if (TargetBlock is not null)
			{
				// 1. Parse the inner markdown using the Singleton Pipeline.
				// This avoids creating a new pipeline per footnote.
				MarkdownDocument InnerDoc = Markdown.Parse(Inline.Data.ToString(), Pipeline);

				// 2. Create the container
				Footnote FootnoteItem = new(null) { Number = Counter };

				// 3. Move blocks from InnerDoc to FootnoteItem
				// We act on a snapshot list to safely modify the tree
				List<Block> Children = [.. InnerDoc];
				foreach (Block Child in Children)
				{
					InnerDoc.Remove(Child); 
					FootnoteItem.Add(Child);
				}

				TargetBlock.Add(FootnoteItem);
			}

			Counter++;
		}
	}
}