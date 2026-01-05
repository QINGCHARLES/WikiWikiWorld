#nullable enable

using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Parser;

namespace WikiWikiWorld.Web;

/// <summary>
/// Middleware to prettify HTML responses by cleaning the DOM and applying smart formatting.
/// </summary>
public sealed class HtmlPrettifyMiddleware(RequestDelegate Next)
{
    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="Context">The HTTP context.</param>
    /// <returns>A task that represents the completion of request processing.</returns>
    public async Task Invoke(HttpContext Context)
    {
        Stream OriginalBodyStream = Context.Response.Body;
        using MemoryStream CaptureStream = new();
        Context.Response.Body = CaptureStream;

        try
        {
            await Next(Context);

            if (!IsHtmlResponse(Context.Response))
            {
                CaptureStream.Seek(0, SeekOrigin.Begin);
                await CaptureStream.CopyToAsync(OriginalBodyStream, Context.RequestAborted);
                return;
            }

            CaptureStream.Seek(0, SeekOrigin.Begin);

            Encoding SelectedEncoding = GetEncoding(Context.Response.ContentType) ?? Encoding.UTF8;
            using StreamReader Reader = new(CaptureStream, SelectedEncoding, true, 1024, leaveOpen: true);
            string RawHtml = await Reader.ReadToEndAsync(Context.RequestAborted);

            string FormattedHtml = await PrettifyHtmlAsync(RawHtml);

            Context.Response.Headers.ContentLength = null;
            Context.Response.Body = OriginalBodyStream;

            byte[] ResponseBytes = SelectedEncoding.GetBytes(FormattedHtml);
            await Context.Response.Body.WriteAsync(ResponseBytes, Context.RequestAborted);
        }
        finally
        {
            Context.Response.Body = OriginalBodyStream;
        }
    }

    private static bool IsHtmlResponse(HttpResponse Response)
    {
        string? ContentType = Response.ContentType;
        return !string.IsNullOrWhiteSpace(ContentType)
            && ContentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    private static Encoding? GetEncoding(string? ContentType)
    {
        if (string.IsNullOrWhiteSpace(ContentType)) return null;
        int Index = ContentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (Index < 0) return null;
        string Charset = ContentType[(Index + "charset=".Length)..].Trim().Trim(';').Trim();
        try { return Encoding.GetEncoding(Charset); } catch { return null; }
    }

    private static async Task<string> PrettifyHtmlAsync(string Html)
    {
        var Parser = new HtmlParser();
        using var Document = await Parser.ParseDocumentAsync(Html);

        // STEP 1: Deep Clean (Normalize Whitespace & Remove Junk)
        Compact(Document);

        // STEP 2: Format (Indent Tags)
        var Formatter = new InlineAwarePrettyFormatter
        {
            Indentation = "\t",
            NewLine = "\n"
        };

        return Document.ToHtml(Formatter);
    }

    private static void Compact(IDocument Document)
    {
        // 1. Layout Tags: Safe to remove purely empty whitespace nodes.
        HashSet<string> LayoutTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "HTML", "HEAD", "BODY", "DIV", "MAIN", "SECTION", "ARTICLE", "HEADER", "FOOTER",
            "UL", "OL", "DL", "TABLE", "THEAD", "TBODY", "TFOOT", "TR",
            "NAV", "ASIDE", "FORM", "FIELDSET", "FIGURE", "SELECT", "OPTGROUP",
            "SVG", "G", "DEFS", "SYMBOL"
        };

        // 2. Content Tags: We trim edges, but keep internal spaces single.
        HashSet<string> ContentTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "P", "H1", "H2", "H3", "H4", "H5", "H6", "LI", "DT", "DD", "TH", "TD", 
            "BLOCKQUOTE", "FIGCAPTION", "OPTION", "LABEL", "LEGEND", "BUTTON", "CAPTION", "SUMMARY"
        };

        // Regex to collapse multiple whitespace (staircasing) into single space
        Regex NormalizeRegex = new(@"\s+");

        // Pre-process: Clean Script and Style tags explicitly
        // FIX: Using .ToList() to avoid ArgumentOutOfRangeException
        var scriptsAndStyles = Document.Descendants<IElement>()
            .Where(e => e.LocalName == "script" || e.LocalName == "style")
            .ToList();

        foreach (var element in scriptsAndStyles)
        {
            if (!string.IsNullOrEmpty(element.TextContent))
            {
                element.TextContent = element.TextContent.Trim();
            }
        }

        // Get all text nodes (Materialized with ToList to avoid concurrent modification)
        var textNodes = Document.Descendants<IText>().ToList();
        
        foreach (var node in textNodes)
        {
            var parent = node.ParentElement;
            if (parent == null) continue;

            // FIX: Robust check for Preserved Scopes (Textarea, Pre, ContentEditable)
            if (IsPreservedScope(parent)) continue;

            string parentTag = parent.LocalName;

            // NORMALIZE: Replace "Text   \n   Text" with "Text Text".
            if (!string.IsNullOrEmpty(node.Data))
            {
                node.Data = NormalizeRegex.Replace(node.Data, " ");
            }

            // CLEANUP: Handle purely whitespace nodes
            if (string.IsNullOrWhiteSpace(node.Data))
            {
                // If it's just empty space inside a Layout tag, delete it.
                // Keeps the DOM tree clean for the formatter.
                if (LayoutTags.Contains(parentTag) || node.Parent == Document)
                {
                    node.Remove();
                    continue;
                }
            }

            // TRIM EDGES: Fixes the "newline after <li>" and "pushed left text in <p>"
            if (ContentTags.Contains(parentTag))
            {
                if (node == parent.FirstChild)
                {
                    node.Data = node.Data.TrimStart();
                }
                if (node == parent.LastChild)
                {
                    node.Data = node.Data.TrimEnd();
                }
                
                if (string.IsNullOrEmpty(node.Data))
                {
                    node.Remove();
                }
            }
        }
    }

    /// <summary>
    /// Checks if the element is within a context where whitespace must be preserved exactly.
    /// Includes &lt;textarea&gt;, &lt;pre&gt;, &lt;code&gt;, and any element with contenteditable="true".
    /// </summary>
    private static bool IsPreservedScope(IElement? element)
    {
        while (element != null)
        {
            var name = element.LocalName;
            
            // Tag-based preservation
            if (name.Equals("pre", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("textarea", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("script", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("style", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("template", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Attribute-based preservation (contenteditable)
            var contentEditable = element.GetAttribute("contenteditable");
            if (string.Equals(contentEditable, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(contentEditable, "", StringComparison.OrdinalIgnoreCase)) // contenteditable="" means true
            {
                return true;
            }

            // CSS-based preservation (basic check)
            var style = element.GetAttribute("style");
            if (!string.IsNullOrEmpty(style) && 
               (style.Contains("white-space: pre", StringComparison.OrdinalIgnoreCase) || 
                style.Contains("white-space:pre", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            element = element.ParentElement;
        }
        return false;
    }
}

/// <summary>
/// Extension methods for adding the <see cref="HtmlPrettifyMiddleware"/>.
/// </summary>
public static class HtmlPrettifyMiddlewareExtensions
{
    /// <summary>
    /// Adds the <see cref="HtmlPrettifyMiddleware"/> to the application's request pipeline.
    /// </summary>
    /// <param name="ApplicationBuilder">The <see cref="IApplicationBuilder"/> instance.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
    public static IApplicationBuilder UseHtmlPrettify(this IApplicationBuilder ApplicationBuilder) =>
        ApplicationBuilder.UseMiddleware<HtmlPrettifyMiddleware>();
}