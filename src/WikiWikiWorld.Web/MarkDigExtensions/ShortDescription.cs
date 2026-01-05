using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A Markdig extension that extracts a short description from the document.
/// </summary>
public sealed class ShortDescriptionExtension : IMarkdownExtension
{
	/// <summary>
	/// The key used to store the short description in the document metadata.
	/// </summary>
	public const string DocumentKey = "ShortDescription";

	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<ShortDescriptionParser>())
		{
			Pipeline.BlockParsers.Add(new ShortDescriptionParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRendererInstance &&
			!HtmlRendererInstance.ObjectRenderers.Contains<ShortDescriptionRenderer>())
		{
			HtmlRendererInstance.ObjectRenderers.Add(new ShortDescriptionRenderer());
		}
	}

	/// <summary>
	/// Extracts the short description from the parsed document and stores it in metadata.
	/// </summary>
	/// <param name="Document">The markdown document.</param>
	public static void Enrich(MarkdownDocument Document)
	{
		ShortDescriptionBlock? Block = Document.Descendants<ShortDescriptionBlock>().FirstOrDefault();
		if (Block is not null)
		{
			Document.SetData(DocumentKey, Block.Content);
		}
	}
}

/// <summary>
/// Parses the {{ShortDescription ...}} block syntax.
/// </summary>
public sealed class ShortDescriptionParser : BlockParser
{
	private const string MarkerStart = "{{ShortDescription";
	private const string MarkerEnd = "}}";

	/// <summary>
	/// Initializes a new instance of the <see cref="ShortDescriptionParser"/> class.
	/// </summary>
	public ShortDescriptionParser()
	{
		OpeningCharacters = ['{'];
	}

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		// Check if the line starts with "{{ShortDescription"
		if (!Processor.Line.Match(MarkerStart))
		{
			return BlockState.None;
		}

		// Find the end marker
		StringSlice Line = Processor.Line;
		int StartPosition = Line.Start + MarkerStart.Length;
		int EndPos = Line.Text.IndexOf(MarkerEnd, StartPosition, StringComparison.Ordinal);

		if (EndPos == -1)
		{
			return BlockState.None;
		}

		// Extract the content
		string Content = Line.Text.Substring(StartPosition, EndPos - StartPosition).Trim();

		// Move cursor forward to avoid infinite loop
		Processor.Line.Start = EndPos + MarkerEnd.Length;

		// Push new block
		ShortDescriptionBlock Block = new(this)
		{
			Content = Content
		};

		Processor.NewBlocks.Push(Block);

		return BlockState.BreakDiscard;
	}
}

/// <summary>
/// A block element representing a short description of the document.
/// </summary>
public sealed class ShortDescriptionBlock : LeafBlock
{
	/// <summary>
	/// Gets or sets the content of the short description.
	/// </summary>
	public string Content { get; set; } = string.Empty;

	/// <summary>
	/// Initializes a new instance of the <see cref="ShortDescriptionBlock"/> class.
	/// </summary>
	/// <param name="Parser">The block parser.</param>
	public ShortDescriptionBlock(BlockParser Parser) : base(Parser)
	{
		ProcessInlines = false;
	}
}

/// <summary>
/// Renders the <see cref="ShortDescriptionBlock"/> (hidden/metadata only).
/// </summary>
public sealed class ShortDescriptionRenderer : HtmlObjectRenderer<ShortDescriptionBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, ShortDescriptionBlock Block)
	{
		// Metadata only, no visible output
		// We output a hidden comment for debugging.
		if (Renderer.EnableHtmlForBlock)
		{
			Renderer.Write($"<!-- Short Description: {Block.Content} -->");
		}
	}
}