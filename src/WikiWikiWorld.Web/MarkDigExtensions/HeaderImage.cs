using System.IO;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using WikiWikiWorld.Web.Pages;

namespace WikiWikiWorld.Web.MarkdigExtensions;

public class HeaderImageBlock : LeafBlock
{
	public required string UrlSlug { get; init; }

	public HeaderImageBlock(BlockParser Parser) : base(Parser) { }
}

public class HeaderImageBlockParser : BlockParser
{
	private const string MarkerStart = "{{HeaderImage ";
	private const string MarkerEnd = "}}";

	public HeaderImageBlockParser() => OpeningCharacters = ['{'];

	public override BlockState TryOpen(BlockProcessor Processor)
	{
		StringSlice Slice = Processor.Line;
		int StartPosition = Slice.Start;

		if (!Slice.Match(MarkerStart))
		{
			return BlockState.None;
		}

		Slice.Start += MarkerStart.Length;
		int EndPos = Slice.Text.IndexOf(MarkerEnd, Slice.Start, StringComparison.Ordinal);
		if (EndPos == -1)
		{
			Slice.Start = StartPosition;
			return BlockState.None;
		}

		string UrlSlug = Slice.Text.Substring(Slice.Start, EndPos - Slice.Start).Trim();

		HeaderImageBlock HeaderBlock = new(this)
		{
			UrlSlug = UrlSlug,
			Column = Processor.Column,
			Span = new SourceSpan(Processor.LineIndex, Processor.LineIndex)
		};

		Processor.NewBlocks.Push(HeaderBlock);
		Slice.Start = EndPos + MarkerEnd.Length;
		return BlockState.BreakDiscard;
	}
}

public class HeaderImageBlockRenderer(int SiteId, string Culture, IArticleRevisionRepository ArticleRepository, IFileRevisionRepository FileRepository, BasePageModel PageModel)
	: HtmlObjectRenderer<HeaderImageBlock>
{
	protected override void Write(HtmlRenderer Renderer, HeaderImageBlock Block)
	{
		ArticleRevision? Article = ArticleRepository
			.GetCurrentBySiteIdCultureAndUrlSlugAsync(SiteId, Culture, Block.UrlSlug.Replace("file:", ""))
			.GetAwaiter()
			.GetResult();

		if (Article is null)
		{
			Renderer.Write($"<!-- Article not found for UrlSlug: {Block.UrlSlug}  -->");
			PageModel.HeaderImage = $"/sitefiles/{SiteId}/missing-image.png";
			return;
		}

		FileRevision? File = FileRepository
			.GetCurrentByCanonicalFileIdAsync(Article.CanonicalFileId!.Value)
			.GetAwaiter()
			.GetResult();

		if (File is null)
		{
			Renderer.Write($"<!-- File not found for CanonicalFileId: {Article.CanonicalFileId} -->");
			PageModel.HeaderImage = $"/sitefiles/{SiteId}/missing-image.png";
			return;
		}

		string ImageUrl = $"/sitefiles/{SiteId}/images/{File.CanonicalFileId}{Path.GetExtension(File.Filename)}";

		PageModel.HeaderImage = ImageUrl;
	}
}

public class HeaderImageExtension(int SiteId, string Culture, IArticleRevisionRepository ArticleRepository, IFileRevisionRepository FileRepository, BasePageModel PageModel)
	: IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<HeaderImageBlockParser>())
		{
			Pipeline.BlockParsers.Insert(0, new HeaderImageBlockParser());
		}
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer &&
			!HtmlRenderer.ObjectRenderers.Any(r => r is HeaderImageBlockRenderer))
		{
			HtmlRenderer.ObjectRenderers.Add(new HeaderImageBlockRenderer(SiteId, Culture, ArticleRepository, FileRepository, PageModel));
		}
	}
}