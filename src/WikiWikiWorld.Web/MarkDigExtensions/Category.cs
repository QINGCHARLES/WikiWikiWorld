using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace WikiWikiWorld.MarkdigExtensions;

public sealed class CategoryInline : LeafInline
{
	public required StringSlice Data { get; init; }
}

public sealed class CategoryExtension(List<Data.Models.Category> Categories) : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.InlineParsers.Contains<CategoryParser>())
		{
			Pipeline.InlineParsers.Add(new CategoryParser());
		}
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRendererInstance &&
			!HtmlRendererInstance.ObjectRenderers.Contains<CategoryRenderer>())
		{
			HtmlRendererInstance.ObjectRenderers.Add(new CategoryRenderer(Categories));
		}
	}
}

public sealed class CategoryParser : InlineParser
{
	private const string MarkerStart = "{{Category ";
	private const string MarkerEnd = "}}";

	public CategoryParser() => OpeningCharacters = ['{'];

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

		string ContentValue = Slice.Text.Substring(Slice.Start, EndPos - Slice.Start).Trim();
		int InlineStart = Processor.GetSourcePosition(Slice.Start, out int Line, out int Column);

		Processor.Inline = new CategoryInline
		{
			Span =
			{
				Start = InlineStart,
				End = InlineStart + (EndPos - Slice.Start)
			},
			Line = Line,
			Column = Column,
			Data = new StringSlice(Slice.Text, Slice.Start, EndPos - 1)
		};

		Slice.Start = EndPos + MarkerEnd.Length;
		return true;
	}
}

public sealed class CategoryRenderer(List<Data.Models.Category> Categories) : HtmlObjectRenderer<CategoryInline>
{
	protected override void Write(HtmlRenderer Renderer, CategoryInline Inline)
	{
		// This simple implementation adds a new category using the content found.
		// Extend this logic if you need to handle custom URL slugs or other attributes.
		Categories.Add(new Data.Models.Category { Title = Inline.Data.ToString() });
	}
}
