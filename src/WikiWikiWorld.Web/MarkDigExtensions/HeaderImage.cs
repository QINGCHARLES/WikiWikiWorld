using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Helpers;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Web.Pages;
using WikiWikiWorld.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.MarkdigExtensions;

public class HeaderImageBlock : LeafBlock
{
    public required string UrlSlug { get; init; }
    public FileRevision? CachedFile { get; set; }

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

public class HeaderImageBlockRenderer(int SiteId, BasePageModel PageModel)
    : HtmlObjectRenderer<HeaderImageBlock>
{
    protected override void Write(HtmlRenderer Renderer, HeaderImageBlock Block)
    {
        FileRevision? File = Block.CachedFile;

        if (File is null)
        {
            Renderer.Write($"<!-- File not found for UrlSlug: {Block.UrlSlug} -->");
            PageModel.HeaderImage = $"/sitefiles/{SiteId}/missing-image.png";
            return;
        }

        string ImageUrl = $"/sitefiles/{SiteId}/images/{File.CanonicalFileId}{Path.GetExtension(File.Filename)}";

        PageModel.HeaderImage = ImageUrl;
    }
}

public class HeaderImageExtension(int SiteId, BasePageModel PageModel)
    : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder Pipeline)
    {
        if (!Pipeline.BlockParsers.Contains<HeaderImageBlockParser>())
        {
            Pipeline.BlockParsers.Add(new HeaderImageBlockParser());
        }
    }

    public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
    {
        if (Renderer is HtmlRenderer HtmlRenderer &&
            !HtmlRenderer.ObjectRenderers.Any(r => r is HeaderImageBlockRenderer))
        {
            HtmlRenderer.ObjectRenderers.Add(new HeaderImageBlockRenderer(SiteId, PageModel));
        }
    }

    public static async Task EnrichAsync(MarkdownDocument Document, WikiWikiWorldDbContext Context, int SiteId, string Culture, CancellationToken CancellationToken = default)
    {
        // 1. Find the blocks
        List<HeaderImageBlock> HeaderBlocks = Document.Descendants<HeaderImageBlock>().ToList();

        if (HeaderBlocks.Count == 0) return;

        // 2. Gather Slugs
        List<string> Slugs = HeaderBlocks
            .Select(b =>
            {
                string s = b.UrlSlug;
                if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) s = s["file:".Length..];
                return SlugHelper.GenerateSlug(s);
            })
            .Distinct()
            .ToList();

        // 3. Batch Query Articles
        List<ArticleRevision> Articles = await Context.ArticleRevisions
            .AsNoTracking()
            .Where(x => x.SiteId == SiteId && x.Culture == Culture && Slugs.Contains(x.UrlSlug) && x.IsCurrent)
            .ToListAsync(CancellationToken);

        Dictionary<string, ArticleRevision> ArticleLookup = Articles.ToDictionary(a => a.UrlSlug, StringComparer.OrdinalIgnoreCase);

        // 4. Gather File IDs
        List<Guid> FileIds = Articles
            .Where(a => a.CanonicalFileId.HasValue)
            .Select(a => a.CanonicalFileId!.Value)
            .Distinct()
            .ToList();

        // 5. Batch Query Files
        List<FileRevision> Files = await Context.FileRevisions
            .AsNoTracking()
            .Where(f => FileIds.Contains(f.CanonicalFileId) && f.IsCurrent == true)
            .ToListAsync(CancellationToken);

        Dictionary<Guid, FileRevision> FileLookup = Files.ToDictionary(f => f.CanonicalFileId);

        // 6. Update Blocks
        foreach (HeaderImageBlock Block in HeaderBlocks)
        {
            string Slug = Block.UrlSlug;
            if (Slug.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) Slug = Slug["file:".Length..];
            Slug = SlugHelper.GenerateSlug(Slug);

            if (ArticleLookup.TryGetValue(Slug, out ArticleRevision? Article) &&
                Article.CanonicalFileId.HasValue &&
                FileLookup.TryGetValue(Article.CanonicalFileId.Value, out FileRevision? File))
            {
                Block.CachedFile = File;
            }
        }
    }
}
