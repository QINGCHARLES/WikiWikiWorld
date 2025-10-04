using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace WikiWikiWorld.Web.MarkdigExtensions;

public sealed class TestExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder Pipeline)
    {
        if (!Pipeline.BlockParsers.Contains<TestParser>())
        {
            Pipeline.BlockParsers.Add(new TestParser());
        }
    }

    public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
    {
        if (Renderer is HtmlRenderer HtmlRendererInstance &&
            !HtmlRendererInstance.ObjectRenderers.Contains<TestRenderer>())
        {
            HtmlRendererInstance.ObjectRenderers.Add(new TestRenderer());
        }
    }
}

public sealed class TestParser : BlockParser
{
    private const string Marker = "{{Test}}";

    public TestParser()
    {
        OpeningCharacters = ['{'];
    }

    public override BlockState TryOpen(BlockProcessor Processor)
    {
        // Check if the line starts with "{{Test}}"
        if (!Processor.Line.Match(Marker))
        {
            return BlockState.None;
        }

        // Move cursor forward to avoid infinite loop
        Processor.Line.Start += Marker.Length;

        // Push new block
        Processor.NewBlocks.Push(new TestBlock(this));

        return BlockState.BreakDiscard;
    }
}

public sealed class TestBlock : LeafBlock
{
    public TestBlock(BlockParser Parser) : base(Parser)
    {
        ProcessInlines = false;
    }
}

public sealed class TestRenderer : HtmlObjectRenderer<TestBlock>
{
    protected override void Write(HtmlRenderer Renderer, TestBlock Block)
    {
        Renderer.WriteLine("<aside>test</aside>");
    }
}