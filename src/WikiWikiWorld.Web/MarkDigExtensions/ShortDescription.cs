using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace WikiWikiWorld.Web.MarkdigExtensions;

public sealed class ShortDescriptionExtension : IMarkdownExtension
{
	private readonly Pages.BasePageModel PageModel;

	public ShortDescriptionExtension(Pages.BasePageModel PageModel)
	{
		this.PageModel = PageModel;
	}

	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<ShortDescriptionParser>())
		{
			Pipeline.BlockParsers.Add(new ShortDescriptionParser());
		}
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRendererInstance &&
			!HtmlRendererInstance.ObjectRenderers.Contains<ShortDescriptionRenderer>())
		{
			HtmlRendererInstance.ObjectRenderers.Add(new ShortDescriptionRenderer(PageModel));
		}
	}
}

public sealed class ShortDescriptionParser : BlockParser
{
	private const string MarkerStart = "{{ShortDescription";
	private const string MarkerEnd = "}}";

	public ShortDescriptionParser()
	{
		OpeningCharacters = ['{'];
	}

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

public sealed class ShortDescriptionBlock : LeafBlock
{
	public string Content { get; set; } = string.Empty;

	public ShortDescriptionBlock(BlockParser Parser) : base(Parser)
	{
		ProcessInlines = false;
	}
}

public sealed class ShortDescriptionRenderer : HtmlObjectRenderer<ShortDescriptionBlock>
{
	private readonly Pages.BasePageModel PageModel;

	public ShortDescriptionRenderer(Pages.BasePageModel PageModel)
	{
		this.PageModel = PageModel;
	}

	protected override void Write(HtmlRenderer Renderer, ShortDescriptionBlock Block)
	{
		// Save the extracted short description into the page model.
		PageModel.MetaDescription = Block.Content;
		// Do not output any HTML - this is metadata
	}
}