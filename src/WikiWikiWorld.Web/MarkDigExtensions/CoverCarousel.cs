using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using System.Text;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A single cover entry parsed from a <see cref="CoverCarouselBlock"/>.
/// </summary>
/// <param name="Slug">The article slug of the cover file.</param>
/// <param name="AltText">Optional author-supplied alt text. When <see langword="null"/>, the renderer falls back to the article title.</param>
/// <param name="Caption">Optional author-supplied caption. Currently stored but not rendered.</param>
public sealed record CoverCarouselEntry(string Slug, string? AltText, string? Caption);

/// <summary>
/// A resolved cover image ready for rendering.
/// </summary>
/// <param name="ImageUrl">The resolved file URL.</param>
/// <param name="AltText">The alt text to write into the <c>&lt;img&gt;</c> tag.</param>
/// <param name="Caption">Optional caption. Currently stored but not rendered.</param>
public sealed record CoverCarouselImage(string ImageUrl, string AltText, string? Caption);

/// <summary>
/// A block element representing a standalone cover carousel.
/// Each cover is one item, separated from the next by the item separator <c>|~|</c>.
/// Within an item, properties (<c>CoverImage=</c>, <c>AltText=</c>, <c>Caption=</c>)
/// are separated by the property separator <c>|#|</c>.
/// </summary>
/// <param name="Parser">The block parser.</param>
public class CoverCarouselBlock(BlockParser Parser) : LeafBlock(Parser)
{
    /// <summary>
    /// Gets the raw content of the carousel block (collected between <c>{{CoverCarousel</c> and <c>}}</c>).
    /// </summary>
    public StringBuilder RawContent { get; } = new();

    /// <summary>
    /// Gets the ordered list of cover entries parsed from the block.
    /// </summary>
    public List<CoverCarouselEntry> Entries { get; } = [];

    /// <summary>
    /// Gets or sets the cached list of resolved cover images to display.
    /// </summary>
    public List<CoverCarouselImage> CachedCoverImages { get; set; } = [];
}

/// <summary>
/// Parses the <c>{{CoverCarousel ...}}</c> block syntax. Accepts either a single-line
/// form or a multi-line form terminated by <c>}}</c>.
/// </summary>
public class CoverCarouselBlockParser : BlockParser
{
    private const string MarkerStart = "{{CoverCarousel";
    private const string MarkerEnd = "}}";

    /// <summary>
    /// Initializes a new instance of the <see cref="CoverCarouselBlockParser"/> class.
    /// </summary>
    public CoverCarouselBlockParser() => OpeningCharacters = ['{'];

    /// <inheritdoc/>
    public override BlockState TryOpen(BlockProcessor Processor)
    {
        StringSlice Slice = Processor.Line;
        if (!Slice.Match(MarkerStart))
        {
            return BlockState.None;
        }

        CoverCarouselBlock CarouselBlock = new(this)
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
                CarouselBlock.RawContent.AppendLine(Remaining[..EndPos]);
                Processor.NewBlocks.Push(CarouselBlock);
                return BlockState.Break;
            }
            CarouselBlock.RawContent.AppendLine(Remaining);
        }

        Processor.NewBlocks.Push(CarouselBlock);
        return BlockState.Continue;
    }

    /// <inheritdoc/>
    public override BlockState TryContinue(BlockProcessor Processor, Block CurrentBlock)
    {
        StringSlice Slice = Processor.Line;
        string Line = Slice.ToString();
        int EndPos = Line.IndexOf(MarkerEnd, StringComparison.Ordinal);
        CoverCarouselBlock CarouselBlock = CurrentBlock as CoverCarouselBlock ?? throw new InvalidOperationException("Block is not a CoverCarouselBlock.");

        if (EndPos != -1)
        {
            CarouselBlock.RawContent.AppendLine(Line[..EndPos]);
            return BlockState.Break;
        }
        CarouselBlock.RawContent.AppendLine(Line);
        return BlockState.Continue;
    }
}

/// <summary>
/// Renders the <see cref="CoverCarouselBlock"/> as an <c>&lt;aside&gt;</c> containing
/// the cover image(s) and a page-position dots row, independently of
/// <see cref="PublicationIssueInfoboxRenderer"/>, but without the infobox property list.
/// </summary>
public class CoverCarouselBlockRenderer : HtmlObjectRenderer<CoverCarouselBlock>
{
    /// <inheritdoc/>
    protected override void Write(HtmlRenderer Renderer, CoverCarouselBlock Block)
    {
        ArgumentNullException.ThrowIfNull(Renderer);
        ArgumentNullException.ThrowIfNull(Block);

        if (Block.CachedCoverImages.Count == 0)
        {
            return;
        }

        // Single image: render it directly with no carousel chrome.
        if (Block.CachedCoverImages.Count == 1)
        {
            CoverCarouselImage Only = Block.CachedCoverImages[0];
            Renderer.Write($"<aside class=\"cover-carousel single\"><img src=\"{Only.ImageUrl}\" alt=\"{Only.AltText}\" /></aside>");
            return;
        }

        RenderCarousel(Renderer, Block.CachedCoverImages);
    }

    /// <summary>
    /// Renders an image carousel using CSS Scroll Snap with JavaScript button navigation
    /// and a row of page-position dots below.
    /// </summary>
    /// <param name="Renderer">The HTML renderer.</param>
    /// <param name="Images">The list of images with URL and alt text.</param>
    private static void RenderCarousel(HtmlRenderer Renderer, List<CoverCarouselImage> Images)
    {
        int ImageCount = Images.Count;

        Renderer.Write("<aside class=\"cover-carousel carousel\">");

        Renderer.Write("<div class=\"slides\">");

        for (int i = 0; i < ImageCount; i++)
        {
            CoverCarouselImage Img = Images[i];

            Renderer.Write("<div class=\"slide\">");
            Renderer.Write($"<img src=\"{Img.ImageUrl}\" alt=\"{Img.AltText}\">");

            if (i > 0)
            {
                Renderer.Write("<button type=\"button\" class=\"nav prev\" aria-label=\"Previous image\" data-dir=\"-1\">");
                Renderer.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\" /><path d=\"M13 15l-3 -3l3 -3\" /><path d=\"M21 12a9 9 0 1 0 -18 0a9 9 0 0 0 18 0z\" /></svg>");
                Renderer.Write("</button>");
            }

            if (i < ImageCount - 1)
            {
                Renderer.Write("<button type=\"button\" class=\"nav next\" aria-label=\"Next image\" data-dir=\"1\">");
                Renderer.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\" /><path d=\"M11 9l3 3l-3 3\" /><path d=\"M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0z\" /></svg>");
                Renderer.Write("</button>");
            }

            Renderer.Write("</div>");
        }

        Renderer.Write("</div>");

        // Page-position dots
        Renderer.Write("<div class=\"dots\">");
        for (int i = 0; i < ImageCount; i++)
        {
            string ActiveClass = i == 0 ? " active" : "";
            Renderer.Write($"<button type=\"button\" class=\"dot{ActiveClass}\" data-index=\"{i}\" aria-label=\"Go to image {i + 1}\"></button>");
        }
        Renderer.Write("</div>");

        Renderer.Write("</aside>");
    }
}

/// <summary>
/// A Markdig extension that adds support for standalone cover carousels via <c>{{CoverCarousel ...}}</c>.
/// Uses <c>CoverImage=slug</c> entries separated by <c>|~|</c>
/// to keep cover images separate from publication facts.
/// </summary>
public class CoverCarouselExtension : IMarkdownExtension
{
    /// <inheritdoc/>
    public void Setup(MarkdownPipelineBuilder Pipeline)
    {
        if (!Pipeline.BlockParsers.Contains<CoverCarouselBlockParser>())
        {
            Pipeline.BlockParsers.Insert(0, new CoverCarouselBlockParser());
        }
    }

    /// <inheritdoc/>
    public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
    {
        if (Renderer is HtmlRenderer HtmlRenderer &&
            !HtmlRenderer.ObjectRenderers.Any(R => R is CoverCarouselBlockRenderer))
        {
            HtmlRenderer.ObjectRenderers.Add(new CoverCarouselBlockRenderer());
        }
    }

    /// <summary>
    /// Asynchronously fetches cover images for all <see cref="CoverCarouselBlock"/> instances in the document.
    /// </summary>
    /// <param name="Document">The markdown document.</param>
    /// <param name="Context">The database context.</param>
    /// <param name="SiteId">The current site ID.</param>
    /// <param name="Culture">The culture code.</param>
    /// <param name="CancellationToken">A cancellation token.</param>
    public static async Task EnrichAsync(MarkdownDocument Document, WikiWikiWorldDbContext Context, int SiteId, string Culture, CancellationToken CancellationToken = default)
    {
        List<CoverCarouselBlock> CarouselBlocks = [.. Document.Descendants<CoverCarouselBlock>()];

        if (CarouselBlocks.Count == 0) return;

        const string ItemSeparator = "|~|";
        const string PropertySeparator = "|#|";
        List<string> AllCoverSlugs = [];

        foreach (CoverCarouselBlock Block in CarouselBlocks)
        {
            string Raw = Block.RawContent.ToString();
            string[] Items = Raw.Split(ItemSeparator, StringSplitOptions.RemoveEmptyEntries);

            foreach (string Item in Items)
            {
                string? Slug = null;
                string? AltText = null;
                string? Caption = null;

                string[] Properties = Item.Split(PropertySeparator, StringSplitOptions.RemoveEmptyEntries);

                foreach (string Property in Properties)
                {
                    string Trimmed = Property.Trim();
                    if (string.IsNullOrEmpty(Trimmed)) continue;

                    int EqPos = Trimmed.IndexOf('=');
                    if (EqPos <= 0) continue;

                    string Key = Trimmed[..EqPos].Trim();
                    string Value = Trimmed[(EqPos + 1)..].Trim();

                    if (Key.Equals("CoverImage", StringComparison.OrdinalIgnoreCase))
                    {
                        string RawSlug = Value;
                        if (RawSlug.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                        {
                            RawSlug = RawSlug["file:".Length..];
                        }
                        if (!string.IsNullOrEmpty(RawSlug))
                        {
                            Slug = RawSlug;
                        }
                    }
                    else if (Key.Equals("AltText", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Value.Length > 0) AltText = Value;
                    }
                    else if (Key.Equals("Caption", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Value.Length > 0) Caption = Value;
                    }
                }

                if (Slug is null) continue;

                Block.Entries.Add(new CoverCarouselEntry(Slug, AltText, Caption));
                AllCoverSlugs.Add(Slug);
            }
        }

        if (AllCoverSlugs.Count == 0) return;

        List<ArticleRevision> Articles = await Context.ArticleRevisions
            .AsNoTracking()
            .Where(x => x.SiteId == SiteId && x.Culture == Culture && AllCoverSlugs.Contains(x.UrlSlug) && x.IsCurrent)
            .ToListAsync(CancellationToken);

        Dictionary<string, ArticleRevision> ArticleLookup = Articles.ToDictionary(a => a.UrlSlug, StringComparer.OrdinalIgnoreCase);

        List<Guid> FileIds = [.. Articles
            .Where(a => a.CanonicalFileId.HasValue)
            .Select(a => a.CanonicalFileId!.Value)
            .Distinct()];

        List<FileRevision> Files = await Context.FileRevisions
            .AsNoTracking()
            .Where(f => FileIds.Contains(f.CanonicalFileId) && f.IsCurrent == true)
            .ToListAsync(CancellationToken);

        Dictionary<Guid, FileRevision> FileLookup = Files.ToDictionary(f => f.CanonicalFileId);

        foreach (CoverCarouselBlock Block in CarouselBlocks)
        {
            foreach (CoverCarouselEntry Entry in Block.Entries)
            {
                if (ArticleLookup.TryGetValue(Entry.Slug, out ArticleRevision? Article) &&
                    Article.CanonicalFileId.HasValue &&
                    FileLookup.TryGetValue(Article.CanonicalFileId.Value, out FileRevision? File))
                {
                    string ImageUrl = ImageUrlHelper.BuildImageUrl(SiteId, File.CanonicalFileId, File.Id, File.Filename);
                    string AltText = Entry.AltText ?? Article.Title ?? "Cover";
                    Block.CachedCoverImages.Add(new CoverCarouselImage(ImageUrl, AltText, Entry.Caption));
                }
                else
                {
                    string AltText = Entry.AltText ?? "Missing Image";
                    Block.CachedCoverImages.Add(new CoverCarouselImage($"/sitefiles/{SiteId}/missing-image.png", AltText, Entry.Caption));
                }
            }
        }
    }
}
