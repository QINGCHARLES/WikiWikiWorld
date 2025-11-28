using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;
using Markdig.Syntax;
using Markdig.Helpers;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace WikiWikiWorld.Web.MarkdigExtensions;

public class ImageInline : LeafInline
{
    public required string UrlSlug { get; init; }
    public string? Layout { get; init; }
    public string? Caption { get; init; }
    public string? Alt { get; init; }
    public ArticleRevision? CachedArticle { get; set; }
    public FileRevision? CachedFile { get; set; }
}

public class ImageInlineParser : InlineParser
{
    private const string MarkerStart = "{{Image ";
    private const string MarkerEnd = "}}";
    private const string AttributeSeparator = "|#|";

    public ImageInlineParser()
    {
        OpeningCharacters = ['{'];
    }

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

        // Extract main data before "|#|"
        int AttributeStart = Slice.Text.IndexOf(AttributeSeparator, Slice.Start, EndPos - Slice.Start, StringComparison.Ordinal);
        string UrlSlug;
        string? Layout = null;
        string? Alt = null;
        string? Caption = null;

        if (AttributeStart != -1)
        {
            UrlSlug = Slice.Text.Substring(Slice.Start, AttributeStart - Slice.Start).Trim();
            string AttributesPart = Slice.Text.Substring(AttributeStart + AttributeSeparator.Length, EndPos - AttributeStart - AttributeSeparator.Length).Trim();

            // Extract attributes
            string[] Attributes = AttributesPart.Split('|', StringSplitOptions.RemoveEmptyEntries);
            foreach (string Attribute in Attributes)
            {
                string[] Parts = Attribute.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (Parts.Length != 2)
                {
                    continue;
                }

                string Key = Parts[0].Trim();
                string Value = Parts[1].Trim();

                if (Key.Equals("Layout", StringComparison.OrdinalIgnoreCase))
                {
                    Layout = Value;
                }
                else if (Key.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    Alt = Value;
                }
                else if (Key.Equals("Caption", StringComparison.OrdinalIgnoreCase))
                {
                    Caption = Value;
                }
            }
        }
        else
        {
            UrlSlug = Slice.Text.Substring(Slice.Start, EndPos - Slice.Start).Trim();
        }

        // Create the inline element
        ImageInline InlineElement = new()
        {
            UrlSlug = UrlSlug,
            Layout = Layout,
            Alt = Alt,
            Caption = Caption
        };

        Processor.Inline = InlineElement;
        Slice.Start = EndPos + MarkerEnd.Length;
        return true;
    }
}

public class ImageInlineRenderer(int SiteId) : HtmlObjectRenderer<ImageInline>
{
    protected override void Write(HtmlRenderer Renderer, ImageInline InlineElement)
    {
        ArticleRevision? Article = InlineElement.CachedArticle;

        if (Article is null)
        {
            Renderer.Write($"<!-- Article not found for UrlSlug: {InlineElement.UrlSlug}  -->");
            Renderer.Write($"<img src=\"/sitefiles/{SiteId}/missing-image.png\" alt=\"Missing Image\" />");
            return;
        }

        if (Article.CanonicalFileId is null)
        {
            Renderer.Write($"<!-- Article has no CanonicalFileId: {InlineElement.UrlSlug} -->");
            Renderer.Write($"<img src=\"/sitefiles/{SiteId}/missing-image.png\" alt=\"Missing Image\" />");
            return;
        }

        FileRevision? File = InlineElement.CachedFile;

        if (File is null)
        {
            Renderer.Write($"<!-- File not found for CanonicalFileId: {Article.CanonicalFileId} -->");
            Renderer.Write($"<img src=\"/sitefiles/{SiteId}/missing-image.png\" alt=\"Missing Image\" />");
            return;
        }

        string ImageUrl = $"/sitefiles/{SiteId}/images/{File.CanonicalFileId}{Path.GetExtension(File.Filename)}";

        // Header images are handled by HeaderImage block; inline images always render <img> with optional layout classes
        string? CssClass = InlineElement.Layout?.ToLowerInvariant() switch
        {
            "full" => "full",
            "breakout" => "breakout",
            "max" => "max",
            _ => null
        };

        // Regular <img> tag with optional class. Use provided Alt if present, otherwise fall back to Article.Title
        string AltText = InlineElement.Alt ?? Article.Title;
        Renderer.Write($"<img src=\"{ImageUrl}\" alt=\"{WebUtility.HtmlEncode(AltText)}\"");
        if (CssClass is not null)
        {
            Renderer.Write($" class=\"{CssClass}\"");
        }
        Renderer.Write(" />");
    }
}

public class ImageExtension(int SiteId) : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder Pipeline)
    {
        if (!Pipeline.InlineParsers.Contains<ImageInlineParser>())
        {
            Pipeline.InlineParsers.Add(new ImageInlineParser());
        }
    }

    public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
    {
        if (Renderer is HtmlRenderer HtmlRenderer &&
            !HtmlRenderer.ObjectRenderers.Any(r => r is ImageInlineRenderer))
        {
            HtmlRenderer.ObjectRenderers.Add(new ImageInlineRenderer(SiteId));
        }
    }

    public static async Task EnrichAsync(MarkdownDocument Document, WikiWikiWorldDbContext Context, int SiteId, string Culture, CancellationToken CancellationToken = default)
    {
        // 1. Find the inlines
        List<ImageInline> ImageInlines = Document.Descendants<ImageInline>().ToList();

        if (ImageInlines.Count == 0) return;

        // 2. Gather Slugs
        List<string> Slugs = ImageInlines
            .Select(i => i.UrlSlug.Replace("file:", ""))
            .Distinct()
            .ToList();

        // 3. Batch Query Articles
        // Note: Using string comparison for slugs as they are strings
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

        // 6. Update Inlines
        foreach (ImageInline Inline in ImageInlines)
        {
            string Slug = Inline.UrlSlug.Replace("file:", "");
            if (ArticleLookup.TryGetValue(Slug, out ArticleRevision? Article))
            {
                Inline.CachedArticle = Article;
                if (Article.CanonicalFileId.HasValue && FileLookup.TryGetValue(Article.CanonicalFileId.Value, out FileRevision? File))
                {
                    Inline.CachedFile = File;
                }
            }
        }
    }
}
