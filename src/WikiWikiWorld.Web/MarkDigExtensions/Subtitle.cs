using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A Markdig extension that extracts a subtitle from the document.
/// Authors add <c>{{Subtitle Some text here}}</c> to display a subtitle
/// paragraph directly below the article title.
/// </summary>
public sealed class SubtitleExtension : IMarkdownExtension
{
	/// <summary>
	/// The document metadata key used to store the subtitle text.
	/// </summary>
	public const string DocumentKey = "Subtitle";

	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<SubtitleBlockParser>())
		{
			Pipeline.BlockParsers.Add(new SubtitleBlockParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRendererInstance &&
			!HtmlRendererInstance.ObjectRenderers.Contains<SubtitleRenderer>())
		{
			HtmlRendererInstance.ObjectRenderers.Add(new SubtitleRenderer());
		}
	}

	/// <summary>
	/// Extracts the subtitle text from the parsed document and stores it in metadata.
	/// Only the first <see cref="SubtitleBlock"/> is used; duplicates are silently ignored.
	/// </summary>
	/// <param name="Document">The parsed markdown document.</param>
	public static void Enrich(MarkdownDocument Document)
	{
		SubtitleBlock? Block = Document.Descendants<SubtitleBlock>().FirstOrDefault();
		if (Block is not null && !string.IsNullOrWhiteSpace(Block.Content))
		{
			Document.SetData(DocumentKey, Block.Content);
		}
	}
}

/// <summary>
/// Parses the <c>{{Subtitle ...}}</c> block syntax.
/// The content between the marker and closing <c>}}</c> becomes the subtitle text.
/// Returns <see cref="BlockState.None"/> if the marker is absent or the end delimiter is missing.
/// </summary>
public sealed class SubtitleBlockParser : BlockParser
{
	private const string MarkerStart = "{{Subtitle ";
	private const string MarkerEnd = "}}";

	/// <summary>
	/// Initializes a new instance of the <see cref="SubtitleBlockParser"/> class.
	/// </summary>
	public SubtitleBlockParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		if (!Processor.Line.Match(MarkerStart))
			return BlockState.None;

		StringSlice Line = Processor.Line;
		int ContentStart = Line.Start + MarkerStart.Length;
		int EndPos = Line.Text.IndexOf(MarkerEnd, ContentStart, StringComparison.Ordinal);

		if (EndPos == -1)
			return BlockState.None;

		string Content = Line.Text.Substring(ContentStart, EndPos - ContentStart).Trim();

		Processor.Line.Start = EndPos + MarkerEnd.Length;

		SubtitleBlock Block = new(this)
		{
			Content = Content
		};

		Processor.NewBlocks.Push(Block);
		return BlockState.BreakDiscard;
	}
}

/// <summary>
/// A leaf block representing a subtitle in a markdown document.
/// </summary>
public sealed class SubtitleBlock : LeafBlock
{
	/// <summary>
	/// Gets or sets the subtitle text.
	/// </summary>
	public string Content { get; set; } = string.Empty;

	/// <summary>
	/// Initializes a new instance of the <see cref="SubtitleBlock"/> class.
	/// </summary>
	/// <param name="Parser">The block parser that created this block.</param>
	public SubtitleBlock(BlockParser Parser) : base(Parser)
	{
		ProcessInlines = false;
	}
}

/// <summary>
/// Renders a <see cref="SubtitleBlock"/> as an HTML comment (metadata only, no visible output).
/// The actual subtitle is rendered by the Razor view from document metadata.
/// </summary>
public sealed class SubtitleRenderer : HtmlObjectRenderer<SubtitleBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, SubtitleBlock Block)
	{
		if (Renderer.EnableHtmlForBlock)
		{
			Renderer.Write($"<!-- Subtitle: {Block.Content} -->");
		}
	}
}
