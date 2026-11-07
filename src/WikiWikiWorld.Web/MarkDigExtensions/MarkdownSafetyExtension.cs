using System.Net;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>Restricts authored Markdown attributes and destinations before HTML rendering, including nested parse passes.</summary>
public sealed class MarkdownSafetyExtension : IMarkdownExtension
{
	/// <summary>Inert presentation/accessibility properties; IDs and classes also have dedicated Markdig storage.</summary>
	private static readonly HashSet<string> AllowedProperties = new(StringComparer.OrdinalIgnoreCase)
	{
		"id", "class", "title", "lang", "dir", "role", "data-kind",
		"width", "height", "colspan", "rowspan", "scope", "align", "start", "reversed"
	};

	/// <summary>Resolves relative destinations for validation only; it is never emitted or fetched.</summary>
	private static readonly Uri RelativeUrlBase = new("https://markdown.invalid/");

	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		Pipeline.DocumentProcessed += Apply;
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		// Keep Markdig's normal renderers; only authored AST values are restricted.
	}

	/// <summary>Restricts parsed authored values before application extensions add their trusted rendering attributes.</summary>
	/// <param name="Document">A freshly parsed article, talk post, or nested Markdown document.</param>
	private static void Apply(MarkdownDocument Document)
	{
		foreach (MarkdownObject Node in Document.Descendants().Prepend(Document))
		{
			HtmlAttributes? Attributes = Node.TryGetAttributes();
			Attributes?.Properties?.RemoveAll(p => !AllowedProperties.Contains(p.Key) && !p.Key.StartsWith("aria-", StringComparison.OrdinalIgnoreCase));
			if (Node is LinkInline Link && !IsAllowedDestination(Link.Url, Link.IsImage))
				Link.Url = string.Empty;
		}
	}

	/// <summary>Allows internal URLs and documented non-executable schemes, with a narrower scheme set for images.</summary>
	/// <param name="Url">The parsed Markdown destination, which may still contain HTML entity spellings.</param>
	/// <param name="IsImage">Whether the destination supplies image bytes rather than a navigable link.</param>
	/// <returns>True for empty/internal, HTTP/HTTPS, or supported non-image FTP/mail/tel destinations.</returns>
	private static bool IsAllowedDestination(string? Url, bool IsImage)
	{
		if (string.IsNullOrEmpty(Url)) return true;
		string Candidate = WebUtility.HtmlDecode(Url);
		if (Candidate.Any(char.IsControl)) return false;
		if (!Uri.TryCreate(RelativeUrlBase, Candidate.Trim(), out Uri? Target)) return false;
		return Target.Scheme is "http" or "https" || (!IsImage && Target.Scheme is "ftp" or "mailto" or "tel");
	}
}
