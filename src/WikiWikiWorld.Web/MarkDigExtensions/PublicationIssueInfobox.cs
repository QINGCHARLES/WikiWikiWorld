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

public class PublicationIssueInfoboxBlock(BlockParser Parser) : LeafBlock(Parser)
{
    public StringBuilder RawContent { get; } = new();
    public Dictionary<string, List<string>> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string ImageUrl, string AltText)> CachedCoverImages { get; set; } = [];
}

public class PublicationIssueInfoboxBlockParser : BlockParser
{
    private const string MarkerStart = "{{PublicationIssueInfobox";
    private const string MarkerEnd = "}}";

    public PublicationIssueInfoboxBlockParser() => OpeningCharacters = ['{'];

    public override BlockState TryOpen(BlockProcessor Processor)
    {
        StringSlice Slice = Processor.Line;
        if (!Slice.Match(MarkerStart))
        {
            return BlockState.None;
        }

        PublicationIssueInfoboxBlock IssueBlock = new(this)
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
                IssueBlock.RawContent.AppendLine(Remaining[..EndPos]);
                Processor.NewBlocks.Push(IssueBlock);
                return BlockState.Break;
            }
            IssueBlock.RawContent.AppendLine(Remaining);
        }

        Processor.NewBlocks.Push(IssueBlock);
        return BlockState.Continue;
    }

    public override BlockState TryContinue(BlockProcessor Processor, Block CurrentBlock)
    {
        StringSlice Slice = Processor.Line;
        string Line = Slice.ToString();
        int EndPos = Line.IndexOf(MarkerEnd, StringComparison.Ordinal);
        PublicationIssueInfoboxBlock IssueBlock = CurrentBlock as PublicationIssueInfoboxBlock ?? throw new InvalidOperationException("Block is not a PublicationIssueInfoboxBlock.");

        if (EndPos != -1)
        {
            IssueBlock.RawContent.AppendLine(Line[..EndPos]);
            return BlockState.Break;
        }
        IssueBlock.RawContent.AppendLine(Line);
        return BlockState.Continue;
    }
}

public class PublicationIssueInfoboxRenderer(int SiteId) : HtmlObjectRenderer<PublicationIssueInfoboxBlock>
{
    // Default number of slides in the standard CSS
    private const int MaxSlides = 3;

    // Maximum number of slides we'll support with inline CSS
    private const int MaxTotalSlides = 10;

    protected override void Write(HtmlRenderer Renderer, PublicationIssueInfoboxBlock Block)
    {
        ArgumentNullException.ThrowIfNull(Renderer);
        ArgumentNullException.ThrowIfNull(Block);

        const string AttributeSeparator = "|#|";
        string Raw = Block.RawContent.ToString();
        string[] Tokens = Raw.Split(AttributeSeparator, StringSplitOptions.RemoveEmptyEntries);

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
            string Value = Trimmed[(EqPos + 1)..].Trim();

            if (!Block.Properties.TryGetValue(Key, out List<string>? List))
            {
                List = [];
                Block.Properties[Key] = List;
            }
            List.Add(Value);
        }

        Renderer.Write("<aside class=\"infobox\">");

        if (Block.CachedCoverImages.Count > 0)
        {
            // If there's only one image, render it directly
            if (Block.CachedCoverImages.Count == 1)
            {
                (string ImageUrl, string AltText) = Block.CachedCoverImages[0];
                Renderer.Write($"<img src=\"{ImageUrl}\" alt=\"{AltText}\" />");
            }
            // If there are multiple images, render the carousel
            else if (Block.CachedCoverImages.Count > 1)
            {
                // Limit to maximum supported slides (including inline CSS extensions)
                int SlidesToRender = Math.Min(Block.CachedCoverImages.Count, MaxTotalSlides);
                RenderCarousel(Renderer, [.. Block.CachedCoverImages.Take(SlidesToRender)]);
            }
        }

        List<KeyValuePair<string, List<string>>> OtherProps = [.. Block.Properties
            .Where(KeyValuePair => !KeyValuePair.Key.Equals("CoverImage", StringComparison.OrdinalIgnoreCase))];

        if (OtherProps.Any())
        {
            Renderer.Write("<ul>");
            foreach (KeyValuePair<string, List<string>> KeyValuePair in OtherProps)
            {
                string Friendly = GetFriendlyName(KeyValuePair.Key);
                foreach (string Val in KeyValuePair.Value)
                {
                    Renderer.Write($"<li><strong>{Friendly}:</strong> {Val}</li>");
                }
            }
            Renderer.Write("</ul>");
        }

        Renderer.Write("</aside>");
    }

    // New method to render the carousel
    private void RenderCarousel(HtmlRenderer Renderer, List<(string ImageUrl, string AltText)> Images)
    {
        int ImageCount = Images.Count;

        // If we have more than MaxSlides, we need to add inline CSS
        if (ImageCount > MaxSlides)
        {
            Renderer.Write(GenerateInlineCssForExtraSlides(ImageCount));
        }

        Renderer.Write("<div class=\"carousel\">");

        // Hidden radio inputs to control slides
        for (int i = 0; i < ImageCount; i++)
        {
            string Checked = i == 0 ? " checked" : "";
            Renderer.Write($"<input type=\"radio\" name=\"carousel\" id=\"slide{i + 1}\"{Checked}>");
        }

        // Slides container
        Renderer.Write("<div class=\"slides\">");

        // Render each slide
        for (int i = 0; i < ImageCount; i++)
        {
            (string ImageUrl, string AltText) = Images[i];
            Renderer.Write($"<div class=\"slide\">");
            Renderer.Write($"<img src=\"{ImageUrl}\" alt=\"{AltText}\">");
            Renderer.Write("</div>");
        }

        Renderer.Write("</div>");

        // Navigation with context-specific controls
        Renderer.Write("<div class=\"nav\">");

        // Generate nav controls for each slide
        for (int i = 0; i < ImageCount; i++)
        {
            int PrevSlide = (i - 1 + ImageCount) % ImageCount + 1;
            int NextSlide = (i + 1) % ImageCount + 1;

            Renderer.Write($"<div class=\"nav-controls nav-slide{i + 1}\">");

            // Previous button
            Renderer.Write($"<label for=\"slide{PrevSlide}\" class=\"prev\">");
            Renderer.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" class=\"icon icon-tabler icons-tabler-outline icon-tabler-circle-chevron-left\"><path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\" /><path d=\"M13 15l-3 -3l3 -3\" /><path d=\"M21 12a9 9 0 1 0 -18 0a9 9 0 0 0 18 0z\" /></svg>");
            Renderer.Write("</label>");

            // Next button
            Renderer.Write($"<label for=\"slide{NextSlide}\" class=\"next\">");
            Renderer.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" class=\"icon icon-tabler icons-tabler-outline icon-tabler-circle-chevron-right\"><path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\" /><path d=\"M11 9l3 3l-3 3\" /><path d=\"M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0z\" /></svg>");
            Renderer.Write("</label>");

            Renderer.Write("</div>");
        }

        Renderer.Write("</div>");

        // Dot indicators below the carousel
        Renderer.Write("<div class=\"dots\">");
        for (int i = 0; i < ImageCount; i++)
        {
            Renderer.Write($"<label for=\"slide{i + 1}\"></label>");
        }
        Renderer.Write("</div>");

        Renderer.Write("</div>");
    }

    // Method to generate inline CSS for slides beyond the default maximum (3)
    private string GenerateInlineCssForExtraSlides(int ImageCount)
    {
        // Only generate CSS for slides beyond 3
        if (ImageCount <= MaxSlides)
        {
            return string.Empty;
        }

        StringBuilder CssBuilder = new();
        CssBuilder.AppendLine("<style>");

        // Show active slide rules
        for (int i = MaxSlides + 1; i <= ImageCount; i++)
        {
            CssBuilder.AppendLine($"#slide{i}:checked ~ .slides .slide:nth-of-type({i}) {{ display: block; }}");
        }

        // Display controls for active slide
        for (int i = MaxSlides + 1; i <= ImageCount; i++)
        {
            CssBuilder.AppendLine($"#slide{i}:checked ~ .nav .nav-slide{i} {{ display: block; }}");
        }

        // Active dot styling
        for (int i = MaxSlides + 1; i <= ImageCount; i++)
        {
            CssBuilder.AppendLine($"#slide{i}:checked ~ .dots label[for=\"slide{i}\"] {{ background-color: var(--uchu-green-5); }}");
        }

        CssBuilder.AppendLine("</style>");
        return CssBuilder.ToString();
    }

    private static string GetFriendlyName(string Key) => Key switch
    {
        "CoverPrice" => "Cover Price",
        "PublicationFormats" => "Publication Formats",
        "CoverDate" => "Cover Date",
        "PrintPublicationDate" => "Print Publication Date",
        "ElectronicPublicationDate" => "Electronic Publication Date",
        "PrintEAN-13" => "Print EAN-13",
        "PrintEAN-2" => "Print EAN-2",
        "ISSN" => "ISSN",
        _ => Key,
    };
}

public class PublicationIssueInfoboxExtension(int SiteId, string Culture, WikiWikiWorldDbContext Context) : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder Pipeline)
    {
        if (!Pipeline.BlockParsers.Contains<PublicationIssueInfoboxBlockParser>())
        {
            Pipeline.BlockParsers.Insert(0, new PublicationIssueInfoboxBlockParser());
        }
    }

    public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
    {
        if (Renderer is HtmlRenderer HtmlRenderer &&
            !HtmlRenderer.ObjectRenderers.Any(R => R is PublicationIssueInfoboxRenderer))
        {
            HtmlRenderer.ObjectRenderers.Add(new PublicationIssueInfoboxRenderer(SiteId));
        }
    }

    public static async Task EnrichAsync(MarkdownDocument Document, WikiWikiWorldDbContext Context, int SiteId, string Culture, CancellationToken CancellationToken = default)
    {
        // 1. Find the blocks
        List<PublicationIssueInfoboxBlock> InfoboxBlocks = Document.Descendants<PublicationIssueInfoboxBlock>().ToList();

        if (InfoboxBlocks.Count == 0) return;

        // 2. Parse properties to find CoverImages
        const string AttributeSeparator = "|#|";
        List<string> AllCoverSlugs = [];

        foreach (PublicationIssueInfoboxBlock Block in InfoboxBlocks)
        {
            string Raw = Block.RawContent.ToString();
            string[] Tokens = Raw.Split(AttributeSeparator, StringSplitOptions.RemoveEmptyEntries);

            foreach (string Token in Tokens)
            {
                string Trimmed = Token.Trim();
                if (string.IsNullOrEmpty(Trimmed)) continue;

                int EqPos = Trimmed.IndexOf('=');
                if (EqPos <= 0) continue;

                string Key = Trimmed[..EqPos].Trim();
                string Value = Trimmed[(EqPos + 1)..].Trim();

                if (!Block.Properties.TryGetValue(Key, out List<string>? List))
                {
                    List = [];
                    Block.Properties[Key] = List;
                }
                List.Add(Value);

                if (Key.Equals("CoverImage", StringComparison.OrdinalIgnoreCase))
                {
                    string Slug = Value;
                    if (Slug.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                    {
                        Slug = Slug["file:".Length..];
                    }
                    AllCoverSlugs.Add(Slug);
                }
            }
        }

        if (AllCoverSlugs.Count == 0) return;

        // 3. Batch Query Articles
        List<ArticleRevision> Articles = await Context.ArticleRevisions
            .AsNoTracking()
            .Where(x => x.SiteId == SiteId && x.Culture == Culture && AllCoverSlugs.Contains(x.UrlSlug) && x.IsCurrent)
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

        // 6. Update Blocks with cached images
        foreach (PublicationIssueInfoboxBlock Block in InfoboxBlocks)
        {
            if (Block.Properties.TryGetValue("CoverImage", out List<string>? CoverImages))
            {
                foreach (string Cover in CoverImages)
                {
                    string Slug = Cover;
                    if (Slug.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                    {
                        Slug = Slug["file:".Length..];
                    }

                    if (ArticleLookup.TryGetValue(Slug, out ArticleRevision? Article) &&
                        Article.CanonicalFileId.HasValue &&
                        FileLookup.TryGetValue(Article.CanonicalFileId.Value, out FileRevision? File))
                    {
                        string ImageUrl = $"/sitefiles/{SiteId}/images/{File.CanonicalFileId}{Path.GetExtension(File.Filename)}";
                        Block.CachedCoverImages.Add((ImageUrl, Article.Title ?? "Publication Cover"));
                    }
                    else
                    {
                        Block.CachedCoverImages.Add(($"/sitefiles/{SiteId}/missing-image.png", "Missing Image"));
                    }
                }
            }
        }
    }
}
