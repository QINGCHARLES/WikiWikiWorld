using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using System.Text;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.MarkdigExtensions;

public class CoverGridBlock(BlockParser Parser) : LeafBlock(Parser)
{
    public StringBuilder RawContent { get; } = new();
    public List<string> UrlSlugs { get; } = [];
    public List<(string ImageUrl, string Title, string UrlSlug)> CachedCoverImages { get; set; } = [];
}

public class CoverGridBlockParser : BlockParser
{
    private const string MarkerStart = "{{CoverGrid";
    private const string MarkerEnd = "}}";

    public CoverGridBlockParser() => OpeningCharacters = ['{'];

    public override BlockState TryOpen(BlockProcessor Processor)
    {
        StringSlice Slice = Processor.Line;
        if (!Slice.Match(MarkerStart))
        {
            return BlockState.None;
        }

        CoverGridBlock CoverBlock = new(this)
        {
            Line = Processor.LineIndex,
            Column = Processor.Column
        };

        Slice.Start += MarkerStart.Length;
        string Remaining = Slice.ToString();
        if (!string.IsNullOrWhiteSpace(Remaining))
        {
            int EndPos = Remaining.IndexOf(MarkerEnd, StringComparison.Ordinal);
            if (EndPos != -1)
            {
                CoverBlock.RawContent.AppendLine(Remaining[..EndPos]);
                Processor.NewBlocks.Push(CoverBlock);
                return BlockState.Break;
            }
            CoverBlock.RawContent.AppendLine(Remaining);
        }

        Processor.NewBlocks.Push(CoverBlock);
        return BlockState.Continue;
    }

    public override BlockState TryContinue(BlockProcessor Processor, Block CurrentBlock)
    {
        StringSlice Slice = Processor.Line;
        string Line = Slice.ToString();
        int EndPos = Line.IndexOf(MarkerEnd, StringComparison.Ordinal);
        CoverGridBlock CoverBlock = CurrentBlock as CoverGridBlock ?? throw new InvalidOperationException("Block is not a CoverGridBlock.");

        if (EndPos != -1)
        {
            CoverBlock.RawContent.AppendLine(Line[..EndPos]);
            return BlockState.Break;
        }
        CoverBlock.RawContent.AppendLine(Line);
        return BlockState.Continue;
    }
}

public class CoverGridRenderer(string Culture) : HtmlObjectRenderer<CoverGridBlock>
{
    protected override void Write(HtmlRenderer Renderer, CoverGridBlock Block)
    {
        ArgumentNullException.ThrowIfNull(Renderer);
        ArgumentNullException.ThrowIfNull(Block);

        // If no cover images were found/cached, don't render anything
        if (!Block.CachedCoverImages.Any())
        {
            return;
        }

        // Render the grid of cover images
        RenderCoverGrid(Renderer, Block.CachedCoverImages);
    }

    private void RenderCoverGrid(HtmlRenderer Renderer, List<(string ImageUrl, string Title, string UrlSlug)> CoverImages)
    {
        // Start the grid container
        Renderer.Write("<div class=\"cover-grid\">");

        // Add grid items
        foreach ((string ImageUrl, string Title, string UrlSlug) in CoverImages)
        {
            Renderer.Write("<div class=\"cover-item\">");
            Renderer.Write($"<a href=\"/{Culture}/{UrlSlug}\">");
            Renderer.Write($"<img src=\"{ImageUrl}\" alt=\"{Title}\" title=\"{Title}\" />");
            Renderer.Write($"<div class=\"cover-title\">{Title}</div>");
            Renderer.Write("</a>");
            Renderer.Write("</div>");
        }

        Renderer.Write("</div>");

        // Add the CSS for the fluid grid (Utopia-inspired approach)
        Renderer.Write("<style>");

        // Main container
        Renderer.Write(".cover-grid {");
        Renderer.Write("  display: grid;");
        Renderer.Write("  grid-template-columns: repeat(auto-fill, minmax(min(100%, 10rem), 1fr));");
        Renderer.Write("  gap: 1rem;");
        Renderer.Write("  width: 100%;");
        Renderer.Write("}");

        // Individual items
        Renderer.Write(".cover-item {");
        Renderer.Write("  display: flex;");
        Renderer.Write("  flex-direction: column;");
        Renderer.Write("  margin-bottom: 1rem;");
        Renderer.Write("}");

        // Links
        Renderer.Write(".cover-item a {");
        Renderer.Write("  display: flex;");
        Renderer.Write("  flex-direction: column;");
        Renderer.Write("  height: 100%;");
        Renderer.Write("  text-decoration: none;");
        Renderer.Write("  color: inherit;");
        Renderer.Write("}");

        // Images
        Renderer.Write(".cover-item img {");
        Renderer.Write("  width: 100%;");
        Renderer.Write("  height: auto;");
        Renderer.Write("  object-fit: cover;");
        Renderer.Write("  aspect-ratio: 0.7/1;"); // Typical magazine cover ratio
        Renderer.Write("}");

        // Titles
        Renderer.Write(".cover-title {");
        Renderer.Write("  margin-top: 0.5rem;");
        Renderer.Write("  text-align: center;");
        Renderer.Write("  font-size: clamp(0.75rem, 1vw, 0.9rem);");
        Renderer.Write("}");

        Renderer.Write("</style>");
    }
}

public class CoverGridExtension(int SiteId, string Culture, WikiWikiWorldDbContext Context) : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder Pipeline)
    {
        if (!Pipeline.BlockParsers.Contains<CoverGridBlockParser>())
        {
            Pipeline.BlockParsers.Insert(0, new CoverGridBlockParser());
        }
    }

    public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
    {
        if (Renderer is HtmlRenderer HtmlRenderer &&
            !HtmlRenderer.ObjectRenderers.Any(R => R is CoverGridRenderer))
        {
            HtmlRenderer.ObjectRenderers.Add(new CoverGridRenderer(Culture));
        }
    }

    public static async Task EnrichAsync(MarkdownDocument Document, WikiWikiWorldDbContext Context, int SiteId, string Culture, CancellationToken CancellationToken = default)
    {
        // 1. Find the blocks
        List<CoverGridBlock> GridBlocks = Document.Descendants<CoverGridBlock>().ToList();

        if (GridBlocks.Count == 0) return;

        // 2. Parse content to find UrlSlugs
        List<string> AllUrlSlugs = [];

        foreach (CoverGridBlock Block in GridBlocks)
        {
            string Raw = Block.RawContent.ToString();
            string[] Lines = Raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (string Line in Lines)
            {
                string Trimmed = Line.Trim();
                if (!string.IsNullOrEmpty(Trimmed))
                {
                    Block.UrlSlugs.Add(Trimmed);
                    AllUrlSlugs.Add(Trimmed);
                }
            }
        }

        if (AllUrlSlugs.Count == 0) return;

        // 3. Batch Query Articles (Level 1)
        List<ArticleRevision> Articles = await Context.ArticleRevisions
            .AsNoTracking()
            .Where(x => x.SiteId == SiteId && x.Culture == Culture && AllUrlSlugs.Contains(x.UrlSlug) && x.IsCurrent)
            .ToListAsync(CancellationToken);

        Dictionary<string, ArticleRevision> ArticleLookup = Articles.ToDictionary(a => a.UrlSlug, StringComparer.OrdinalIgnoreCase);

        // 4. Extract Cover Image Slugs from Articles
        List<string> CoverImageSlugs = [];
        Dictionary<string, string> ArticleToCoverSlug = []; // ArticleSlug -> CoverImageSlug

        foreach (ArticleRevision Article in Articles)
        {
            if (!Article.Text.Contains("{{PublicationIssueInfobox", StringComparison.OrdinalIgnoreCase)) continue;

            string? CoverImageSlug = ExtractCoverImageFromInfobox(Article.Text);
            if (!string.IsNullOrEmpty(CoverImageSlug))
            {
                CoverImageSlugs.Add(CoverImageSlug);
                ArticleToCoverSlug[Article.UrlSlug] = CoverImageSlug;
            }
        }

        if (CoverImageSlugs.Count == 0) return;

        // 5. Batch Query Cover Articles (Level 2)
        List<ArticleRevision> CoverArticles = await Context.ArticleRevisions
            .AsNoTracking()
            .Where(x => x.SiteId == SiteId && x.Culture == Culture && CoverImageSlugs.Contains(x.UrlSlug) && x.IsCurrent)
            .ToListAsync(CancellationToken);

        Dictionary<string, ArticleRevision> CoverArticleLookup = CoverArticles.ToDictionary(a => a.UrlSlug, StringComparer.OrdinalIgnoreCase);

        // 6. Gather File IDs
        List<Guid> FileIds = CoverArticles
            .Where(a => a.CanonicalFileId.HasValue)
            .Select(a => a.CanonicalFileId!.Value)
            .Distinct()
            .ToList();

        // 7. Batch Query Files
        List<FileRevision> Files = await Context.FileRevisions
            .AsNoTracking()
            .Where(f => FileIds.Contains(f.CanonicalFileId) && f.IsCurrent == true)
            .ToListAsync(CancellationToken);

        Dictionary<Guid, FileRevision> FileLookup = Files.ToDictionary(f => f.CanonicalFileId);

        // 8. Update Blocks
        foreach (CoverGridBlock Block in GridBlocks)
        {
            foreach (string UrlSlug in Block.UrlSlugs)
            {
                if (!ArticleLookup.TryGetValue(UrlSlug, out ArticleRevision? Article))
                {
                    Block.CachedCoverImages.Add(($"/sitefiles/{SiteId}/cover-missing.png", "Missing Article", UrlSlug));
                    continue;
                }

                if (ArticleToCoverSlug.TryGetValue(Article.UrlSlug, out string? CoverSlug) &&
                    CoverArticleLookup.TryGetValue(CoverSlug, out ArticleRevision? CoverArticle) &&
                    CoverArticle.CanonicalFileId.HasValue &&
                    FileLookup.TryGetValue(CoverArticle.CanonicalFileId.Value, out FileRevision? File))
                {
                    string ImageUrl = $"/sitefiles/{SiteId}/images/{File.CanonicalFileId}{Path.GetExtension(File.Filename)}";
                    Block.CachedCoverImages.Add((ImageUrl, Article.Title ?? "Title", UrlSlug));
                }
                else
                {
                    Block.CachedCoverImages.Add(($"/sitefiles/{SiteId}/cover-missing.png", Article.Title ?? "Title", UrlSlug));
                }
            }
        }
    }

    private static string? ExtractCoverImageFromInfobox(string Text)
    {
        // Find the start of the infobox
        int Start = Text.IndexOf("{{PublicationIssueInfobox", StringComparison.OrdinalIgnoreCase);
        if (Start == -1)
        {
            return null;
        }

        // Find the end of the infobox
        int End = Text.IndexOf("}}", Start, StringComparison.Ordinal);
        if (End == -1)
        {
            return null;
        }

        // Extract the infobox content
        string InfoboxContent = Text[Start..(End + 2)];

        // Special handling for CoverImage which might be the first property
        int CoverImagePos = InfoboxContent.IndexOf("CoverImage=", StringComparison.OrdinalIgnoreCase);
        if (CoverImagePos != -1)
        {
            // Extract from after "CoverImage=" to the next separator or end
            int ValueStart = CoverImagePos + "CoverImage=".Length;
            int ValueEnd = InfoboxContent.IndexOf("|#|", ValueStart, StringComparison.Ordinal);

            if (ValueEnd == -1)
            {
                // If no separator found, extract until the end marker
                ValueEnd = InfoboxContent.IndexOf("}}", ValueStart, StringComparison.Ordinal);
            }

            if (ValueEnd != -1)
            {
                string Value = InfoboxContent[ValueStart..ValueEnd].Trim();

                // Handle "file:" prefix
                if (Value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    Value = Value["file:".Length..];
                }

                return Value;
            }
        }

        // If we get here, try the token-based approach as fallback
        const string AttributeSeparator = "|#|";
        string[] Tokens = InfoboxContent.Split(AttributeSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (string Token in Tokens)
        {
            string Trimmed = Token.Trim();
            if (string.IsNullOrEmpty(Trimmed))
            {
                continue;
            }

            int EqPos = Trimmed.IndexOf('=');
            if (EqPos <= 0)
            {
                continue;
            }

            string Key = Trimmed[..EqPos].Trim();
            // Check if the key ends with "CoverImage" for first token case
            if (Key.EndsWith("CoverImage", StringComparison.OrdinalIgnoreCase))
            {
                string Value = Trimmed[(EqPos + 1)..].Trim();

                // Handle "file:" prefix
                if (Value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    Value = Value["file:".Length..];
                }

                return Value;
            }
        }

        return null;
    }
}
