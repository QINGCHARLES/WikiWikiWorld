using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A hatnote: one short italic line at the top of an article that points readers to another article sharing its name,
/// such as <c>{{Hatnote For the 1955–1957 men's magazine, see [Playgirl (1955–1957)](/playgirl-1955-1957).}}</c>.
/// Its text goes through the article's normal inline Markdown, so links and emphasis work.
/// </summary>
public sealed class HatnoteBlock : LeafBlock
{
	/// <summary>Creates a hatnote whose inline Markdown is parsed in the article's inline pass.</summary>
	/// <param name="Parser">The parser that found the directive.</param>
	/// <param name="Text">The text between <c>{{Hatnote</c> and the closing braces, trimmed.</param>
	public HatnoteBlock(BlockParser Parser, string Text) : base(Parser)
	{
		ProcessInlines = true;
		Lines = new StringLineGroup(Text);
	}
}

/// <summary>
/// Parses <c>{{Hatnote …}}</c> written on a line of its own. A directive with no text, or with more text after its
/// closing braces, is not a hatnote and stays visible as ordinary text.
/// </summary>
public sealed class HatnoteBlockParser : BlockParser
{
	/// <summary>The directive's opening, including the space before its text.</summary>
	internal const string MarkerStart = "{{Hatnote ";

	/// <summary>Registers the opening brace that starts the directive.</summary>
	public HatnoteBlockParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		if (Processor.IsCodeIndent) return BlockState.None;

		StringSlice Slice = Processor.Line;
		if (!Slice.Match(MarkerStart)) return BlockState.None;

		string Line = Slice.Text[Slice.Start..(Slice.End + 1)];
		int Close = DirectiveSyntax.FindClosingDelimiter(Line, MarkerStart.Length);
		if (Close < 0 || !string.IsNullOrWhiteSpace(Line[(Close + 2)..])) return BlockState.None;

		string Text = Line[MarkerStart.Length..Close].Trim();
		if (Text.Length == 0) return BlockState.None;

		Processor.NewBlocks.Push(new HatnoteBlock(this, Text) { Line = Processor.LineIndex, Column = Processor.Column });
		Processor.Line.Start = Processor.Line.End + 1;
		return BlockState.BreakDiscard;
	}
}

/// <summary>
/// Renders a hatnote as a note above the lead. It is a <c>div</c>, not a paragraph, so the lead's first paragraph keeps
/// its first-line styling.
/// </summary>
public sealed class HatnoteRenderer : HtmlObjectRenderer<HatnoteBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, HatnoteBlock Block)
	{
		Renderer.Write("<div class=\"hatnote\" role=\"note\">");
		Renderer.WriteLeafInline(Block);
		Renderer.WriteLine("</div>");
	}
}

/// <summary>Adds <c>{{Hatnote …}}</c> to the article pipeline and reports misplaced hatnotes on save.</summary>
public sealed class HatnoteExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<HatnoteBlockParser>())
		{
			Pipeline.BlockParsers.Insert(0, new HatnoteBlockParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer Html && !Html.ObjectRenderers.Contains<HatnoteRenderer>())
		{
			Html.ObjectRenderers.Add(new HatnoteRenderer());
		}
	}

	/// <summary>
	/// Lists hatnotes an author should move: a hatnote belongs among the article's top lines, before the lead and any
	/// heading, so one written after article content no longer sits at the top of the page.
	/// </summary>
	/// <param name="Document">The parsed article.</param>
	/// <returns>One message per misplaced hatnote, in document order; empty when there are none.</returns>
	public static IReadOnlyList<string> FindProblems(MarkdownDocument Document)
	{
		ArgumentNullException.ThrowIfNull(Document);

		List<string> Problems = [];
		bool ContentSeen = false;
		foreach (Block Block in Document)
		{
			if (Block is HatnoteBlock Hatnote)
			{
				if (ContentSeen)
				{
					Problems.Add($"Line {Hatnote.Line + 1}: {{{{Hatnote}}}} comes after the article's text. Move it up to the "
						+ "top lines, after {{Eyebrow}} and any {{HeaderImage}}, so it appears above the lead.");
				}
			}
			else if (!IsTopLine(Block))
			{
				ContentSeen = true;
			}
		}
		return Problems;
	}

	/// <summary>Tells whether a block is one of the metadata lines that may come before a hatnote.</summary>
	/// <param name="Block">A top-level block of the article.</param>
	/// <returns><see langword="true"/> for the short description, eyebrow, header image, subtitle and blank lines.</returns>
	private static bool IsTopLine(Block Block) =>
		Block is ShortDescriptionBlock or EyebrowBlock or HeaderImageBlock or SubtitleBlock or BlankLineBlock or LinkReferenceDefinitionGroup;
}
