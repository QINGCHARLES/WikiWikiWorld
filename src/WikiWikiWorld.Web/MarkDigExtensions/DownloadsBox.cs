using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using System.Globalization;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.MarkdigExtensions;

// Block-level element for the DownloadsBox
/// <summary>
/// Block-level element representing a download box for a specific file hash.
/// </summary>
/// <param name="Parser">The block parser used to create this block.</param>
public class DownloadsBoxBlock(BlockParser Parser) : ContainerBlock(Parser)
{
    /// <summary>
    /// Gets or initializes the SHA256 hash of the file to download.
    /// </summary>
    public required String Hash { get; init; }

    /// <summary>
    /// Gets or sets the cached download URL information.
    /// </summary>
    public DownloadUrl? CachedDownload { get; set; }
}

// Block parser for DownloadsBoxBlock
/// <summary>
/// Parser for the {{DownloadsBox ...}} block.
/// </summary>
public class DownloadsBoxBlockParser : BlockParser
{
    private const String MarkerStart = "{{DownloadsBox ";
    private const String MarkerEnd = "}}";

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadsBoxBlockParser"/> class.
    /// </summary>
    public DownloadsBoxBlockParser()
    {
        // Trigger when a line starts with '{'
        OpeningCharacters = ['{'];
    }

    /// <inheritdoc/>
    public override BlockState TryOpen(BlockProcessor Processor)
    {
        Markdig.Helpers.StringSlice Slice = Processor.Line;
        Slice.TrimStart(); // Trim leading whitespace
        Int32 StartPosition = Slice.Start;

        if (!Slice.Match(MarkerStart))
        {
            return BlockState.None;
        }

        Slice.Start += MarkerStart.Length;
        Int32 EndPos = Slice.Text.IndexOf(MarkerEnd, Slice.Start, StringComparison.Ordinal);
        if (EndPos == -1)
        {
            Slice.Start = StartPosition;
            return BlockState.None;
        }

        String HashContent = Slice.Text.Substring(Slice.Start, EndPos - Slice.Start).Trim();
        if (String.IsNullOrWhiteSpace(HashContent))
        {
            Slice.Start = StartPosition;
            return BlockState.None;
        }

        DownloadsBoxBlock Block = new(this)
        {
            Hash = HashContent,
            Column = Processor.Column,
            Line = Processor.LineIndex,
            Span = new SourceSpan(Processor.Start, Processor.Line.End)
        };

        Processor.NewBlocks.Push(Block);
        Slice.Start = EndPos + MarkerEnd.Length;
        return BlockState.BreakDiscard;
    }
}

// Block renderer for DownloadsBoxBlock
/// <summary>
/// Renders the <see cref="DownloadsBoxBlock"/> as an HTML aside element.
/// </summary>
public class DownloadsBoxBlockRenderer : HtmlObjectRenderer<DownloadsBoxBlock>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadsBoxBlockRenderer"/> class.
    /// </summary>
    public DownloadsBoxBlockRenderer() { }

    /// <inheritdoc/>
    protected override void Write(HtmlRenderer Renderer, DownloadsBoxBlock Block)
    {
        ArgumentNullException.ThrowIfNull(Renderer);
        ArgumentNullException.ThrowIfNull(Block);

        // Start the downloads box
        Renderer.Write("<aside class=\"downloads-box\">");

        if (Block.CachedDownload is not null)
        {
            RenderDownload(Renderer, Block.CachedDownload);
        }
        else
        {
            Renderer.Write("<p class=\"no-downloads\">Download file not available.</p>");
        }

        Renderer.Write("</aside>");
    }

    /// <summary>
    /// Renders a single download entry with file info and download button.
    /// </summary>
    /// <param name="Renderer">The HTML renderer.</param>
    /// <param name="Download">The download URL information.</param>
    private void RenderDownload(HtmlRenderer Renderer, DownloadUrl Download)
    {
        String QualityText = Download.Quality.HasValue ? $"Quality: {Download.Quality}" : "";
        String FileSizeText = FormatFileSize(Download.FileSizeBytes);

        // Render file info
        Renderer.Write("<div class=\"file-info\">");
        Renderer.Write("<div class=\"file-icon\"><svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1.5\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\"/><path d=\"M14 3v4a1 1 0 0 0 1 1h4\" /><path d=\"M5 12v-7a2 2 0 0 1 2 -2h7l5 5v4\" /><path d=\"M5 18h1.5a1.5 1.5 0 0 0 0 -3h-1.5v6\" /><path d=\"M17 18h2\" /><path d=\"M20 15h-3v6\" /><path d=\"M11 15v6h1a2 2 0 0 0 2 -2v-2a2 2 0 0 0 -2 -2h-1z\" /></svg></div>");
        Renderer.Write("<div class=\"text-content\">");
        Renderer.Write($"<p class=\"filename\">{HtmlEscape(Download.Filename)}</p>");
        Renderer.Write($"<p class=\"filesize\">{FileSizeText}</p>");
        if (!String.IsNullOrEmpty(QualityText))
        {
            Renderer.Write($"<p class=\"quality\">{QualityText}/5</p>");
        }
        // Note: Provider text logic removed for simplicity as it requires another DB lookup or join. 
        // If needed, it should be fetched in EnrichAsync.
        Renderer.Write("</div>");
        Renderer.Write("</div>");

        // Render download button
        Renderer.Write($"<a class=\"button\" data-hash=\"{Download.HashSha256}\" href=\"{Download.DownloadUrls}\" target=\"_blank\" rel=\"noopener noreferrer\">");
        Renderer.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" class=\"icon icon-tabler icons-tabler-outline icon-tabler-download\">");
        Renderer.Write("<path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\"/>");
        Renderer.Write("<path d=\"M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2 -2v-2\" />");
        Renderer.Write("<path d=\"M7 11l5 5l5 -5\" />");
        Renderer.Write("<path d=\"M12 4l0 12\" />");
        Renderer.Write("</svg>Download</a>");
    }

    /// <summary>
    /// Formats a file size in bytes to a human-readable string.
    /// </summary>
    /// <param name="Bytes">The file size in bytes.</param>
    /// <returns>A formatted string like "1.5 MB".</returns>
    private static String FormatFileSize(Int64 Bytes)
    {
        String[] Suffixes = { "B", "KB", "MB", "GB", "TB" };
        Int32 Order = 0;
        Double Size = Bytes;

        while (Size >= 1024 && Order < Suffixes.Length - 1)
        {
            Order++;
            Size /= 1024;
        }

        String FormattedSize = Size < 10
            ? Size.ToString("0.##", CultureInfo.InvariantCulture)
            : Size.ToString("0", CultureInfo.InvariantCulture);

        return $"{FormattedSize} {Suffixes[Order]}";
    }

    /// <summary>
    /// HTML-escapes a string for safe rendering.
    /// </summary>
    /// <param name="Text">The text to escape.</param>
    /// <returns>The HTML-escaped text.</returns>
    private static String HtmlEscape(String Text) => Text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#39;");
}

// Extension to register the DownloadsBox block parser and renderer
/// <summary>
/// Extension to register the DownloadsBox block parser and renderer.
/// </summary>
public class DownloadsBoxExtension : IMarkdownExtension
{
    /// <inheritdoc/>
    public void Setup(MarkdownPipelineBuilder Pipeline)
    {
        if (!Pipeline.BlockParsers.Contains<DownloadsBoxBlockParser>())
        {
            Pipeline.BlockParsers.Add(new DownloadsBoxBlockParser());
        }
    }

    /// <inheritdoc/>
    public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
    {
        if (Renderer is HtmlRenderer HtmlRenderer &&
            !HtmlRenderer.ObjectRenderers.Any(r => r is DownloadsBoxBlockRenderer))
        {
            HtmlRenderer.ObjectRenderers.Add(new DownloadsBoxBlockRenderer());
        }
    }

    /// <summary>
    /// Asynchronously fetches download information for all download boxes in the document.
    /// </summary>
    /// <param name="Document">The markdown document to process.</param>
    /// <param name="Context">The database context.</param>
    /// <param name="SiteId">The current site ID.</param>
    /// <param name="CancellationToken">A cancellation token.</param>
    public static async Task EnrichAsync(MarkdownDocument Document, WikiWikiWorldDbContext Context, int SiteId, CancellationToken CancellationToken = default)
    {
        // 1. Find the blocks specifically for this extension
        List<DownloadsBoxBlock> DownloadBlocks = Document.Descendants<DownloadsBoxBlock>().ToList();

        if (DownloadBlocks.Count == 0) return;

        // 2. Gather IDs
        List<string> Hashes = DownloadBlocks.Select(b => b.Hash).Distinct().ToList();

        // 3. Batch Query
        Dictionary<string, DownloadUrl> Downloads = await Context.DownloadUrls
            .AsNoTracking()
            .Where(d => d.SiteId == SiteId && Hashes.Contains(d.HashSha256))
            .ToDictionaryAsync(d => d.HashSha256, CancellationToken);

        // 4. Update Blocks
        foreach (DownloadsBoxBlock Block in DownloadBlocks)
        {
            if (Downloads.TryGetValue(Block.Hash, out DownloadUrl? Data))
            {
                Block.CachedDownload = Data;
            }
        }
    }
}