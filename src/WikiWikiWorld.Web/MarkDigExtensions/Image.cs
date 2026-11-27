using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.EntityFrameworkCore;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Web.Helpers;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// Shared AST contract implemented by both standalone <see cref="ImageBlock"/> and inline <see cref="ImageInline"/> elements.
/// </summary>
internal interface IImageDirectiveNode
{
	/// <summary>Gets the URL slug of the target file article.</summary>
	string UrlSlug { get; }

	/// <summary>Gets the raw <c>Layout=</c> value, if specified.</summary>
	string? Layout { get; }

	/// <summary>Gets the raw <c>Size=</c> value, if specified.</summary>
	string? Size { get; }

	/// <summary>Gets the raw <c>Frame=</c> value, if specified.</summary>
	string? Frame { get; }

	/// <summary>Gets the optional alternate text for the image.</summary>
	string? Alt { get; }

	/// <summary>Gets the optional caption text (with inline Markdown).</summary>
	string? Caption { get; }

	/// <summary>Gets the raw <c>CaptionPosition=</c> value, if specified.</summary>
	string? CaptionPosition { get; }

	/// <summary>Gets the optional attribution credit text (with inline Markdown).</summary>
	string? Credit { get; }

	/// <summary>Gets the raw <c>Link=</c> value, if specified.</summary>
	string? Link { get; }

	/// <summary>Gets any unrecognized property names encountered during parsing.</summary>
	IReadOnlyList<string> UnknownProperties { get; }

	/// <summary>Gets the zero-based source line index where the directive appears.</summary>
	int Line { get; }

	/// <summary>Gets or sets the resolved file article revision.</summary>
	ArticleRevision? CachedArticle { get; set; }

	/// <summary>Gets or sets the resolved file revision.</summary>
	FileRevision? CachedFile { get; set; }

	/// <summary>Gets or sets the current site identifier.</summary>
	int SiteId { get; set; }
}

/// <summary>
/// Parsed property bag extracted from a <c>{{Image ...}}</c> directive payload.
/// </summary>
/// <param name="UrlSlug">The trimmed target file article slug.</param>
/// <param name="Layout">The raw <c>Layout=</c> value, if provided.</param>
/// <param name="Size">The raw <c>Size=</c> value, if provided.</param>
/// <param name="Frame">The raw <c>Frame=</c> value, if provided.</param>
/// <param name="Alt">The trimmed alternate text, if non-empty.</param>
/// <param name="Caption">The trimmed caption Markdown, if non-empty.</param>
/// <param name="CaptionPosition">The raw <c>CaptionPosition=</c> value, if provided.</param>
/// <param name="Credit">The trimmed credit Markdown, if non-empty.</param>
/// <param name="Link">The raw <c>Link=</c> value, if provided.</param>
/// <param name="UnknownProperties">Unrecognized property keys in source order.</param>
internal sealed record class ParsedImageDirective(
	string UrlSlug,
	string? Layout,
	string? Size,
	string? Frame,
	string? Alt,
	string? Caption,
	string? CaptionPosition,
	string? Credit,
	string? Link,
	IReadOnlyList<string> UnknownProperties);

/// <summary>
/// A standalone block element representing a wiki-hosted <c>{{Image ...}}</c> figure.
/// </summary>
/// <param name="Parser">The block parser that created this node.</param>
public sealed class ImageBlock(BlockParser Parser) : LeafBlock(Parser), IImageDirectiveNode
{
	/// <summary>Gets or initializes the URL slug of the target file article.</summary>
	public required string UrlSlug { get; init; }

	/// <summary>Gets or initializes the optional layout style (<c>center</c>, <c>aside</c>, <c>start</c>, <c>full</c>, <c>breakout</c>, or <c>max</c>).</summary>
	public string? Layout { get; init; }

	/// <summary>Gets or initializes the optional width preset (<c>small</c>, <c>medium</c>, or <c>large</c>).</summary>
	public string? Size { get; init; }

	/// <summary>Gets or initializes the optional frame treatment (<c>none</c>, <c>border</c>, <c>paper</c>, or <c>dark</c>).</summary>
	public string? Frame { get; init; }

	/// <summary>Gets or initializes the alternate text for the image.</summary>
	public string? Alt { get; init; }

	/// <summary>Gets or initializes the optional caption for the image.</summary>
	public string? Caption { get; init; }

	/// <summary>Gets or initializes the optional caption placement (<c>bottom</c> or <c>top</c>).</summary>
	public string? CaptionPosition { get; init; }

	/// <summary>Gets or initializes the optional attribution credit for the image.</summary>
	public string? Credit { get; init; }

	/// <summary>Gets or initializes the optional link mode (<c>file</c> or <c>none</c>).</summary>
	public string? Link { get; init; }

	/// <summary>Gets any unrecognized property names found on the directive.</summary>
	public IReadOnlyList<string> UnknownProperties { get; init; } = [];

	/// <summary>Gets or sets the cached article revision for this image.</summary>
	public ArticleRevision? CachedArticle { get; set; }

	/// <summary>Gets or sets the cached file revision for this image.</summary>
	public FileRevision? CachedFile { get; set; }

	/// <summary>Gets or sets the site ID context for this image.</summary>
	public int SiteId { get; set; }
}

/// <summary>
/// A Markdown inline element representing an embedded image from the WikiWikiWorld database within prose.
/// </summary>
public sealed class ImageInline : LeafInline, IImageDirectiveNode
{
	/// <summary>Gets or initializes the URL slug of the image article.</summary>
	public required string UrlSlug { get; init; }

	/// <summary>Gets or initializes the optional layout style (e.g., <c>full</c>, <c>breakout</c>).</summary>
	public string? Layout { get; init; }

	/// <summary>Gets or initializes the optional width preset (<c>small</c>, <c>medium</c>, or <c>large</c>).</summary>
	public string? Size { get; init; }

	/// <summary>Gets or initializes the optional frame treatment (<c>none</c>, <c>border</c>, <c>paper</c>, or <c>dark</c>).</summary>
	public string? Frame { get; init; }

	/// <summary>Gets or initializes the optional caption for the image.</summary>
	public string? Caption { get; init; }

	/// <summary>Gets or initializes the optional caption placement (<c>bottom</c> or <c>top</c>).</summary>
	public string? CaptionPosition { get; init; }

	/// <summary>Gets or initializes the alternate text for the image.</summary>
	public string? Alt { get; init; }

	/// <summary>Gets or initializes the optional attribution credit for the image.</summary>
	public string? Credit { get; init; }

	/// <summary>Gets or initializes the optional link mode (<c>file</c> or <c>none</c>).</summary>
	public string? Link { get; init; }

	/// <summary>Gets any unrecognized property names found on the directive.</summary>
	public IReadOnlyList<string> UnknownProperties { get; init; } = [];

	/// <summary>Gets or sets the cached article revision for this image.</summary>
	public ArticleRevision? CachedArticle { get; set; }

	/// <summary>Gets or sets the cached file revision for this image.</summary>
	public FileRevision? CachedFile { get; set; }

	/// <summary>Gets or sets the site ID context for this image.</summary>
	public int SiteId { get; set; }
}

/// <summary>
/// Parses <c>{{Image ...}}</c> payloads consistently for both standalone blocks and inline occurrences.
/// </summary>
internal static partial class ImageDirectiveParser
{
	internal const string MarkerStart = "{{Image ";

	private static readonly FrozenSet<string> AllowedLayouts =
		FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "center", "aside", "start", "full", "breakout", "max");

	private static readonly FrozenSet<string> AllowedSizes =
		FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "small", "medium", "large");

	private static readonly FrozenSet<string> AllowedFrames =
		FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "none", "border", "paper", "dark");

	private static readonly FrozenSet<string> AllowedLinks =
		FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "file", "none");

	private static readonly FrozenSet<string> AllowedCaptionPositions =
		FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "bottom", "top");

	/// <summary>
	/// Parses the inner payload (between <c>{{Image </c> and <c>}}</c>) into structured options.
	/// </summary>
	/// <param name="Payload">The raw directive payload.</param>
	/// <param name="Directive">The parsed directive when the slug is non-empty.</param>
	/// <returns><see langword="true"/> when a non-empty file slug was present; otherwise, <see langword="false"/>.</returns>
	internal static bool TryParsePayload(string Payload, [NotNullWhen(true)] out ParsedImageDirective? Directive)
	{
		Directive = null;
		string[] Segments = [.. DirectiveSyntax.SplitProperties(Payload)];
		if (Segments.Length == 0)
		{
			return false;
		}

		string UrlSlug = Segments[0].Trim();
		if (string.IsNullOrWhiteSpace(UrlSlug))
		{
			return false;
		}

		string? Layout = null;
		string? Size = null;
		string? Frame = null;
		string? Alt = null;
		string? Caption = null;
		string? CaptionPosition = null;
		string? Credit = null;
		string? Link = null;
		List<string> UnknownProperties = [];

		for (int i = 1; i < Segments.Length; i++)
		{
			// Preserve legacy bare '|' property separators only when followed by a recognized Key= assignment.
			string[] SubProperties = LegacyPropertySeparatorRegex().Split(Segments[i]);
			foreach (string RawProperty in SubProperties)
			{
				int EqualsIndex = RawProperty.IndexOf('=');
				if (EqualsIndex <= 0)
				{
					continue;
				}

				string Key = RawProperty[..EqualsIndex].Trim();
				string Value = RawProperty[(EqualsIndex + 1)..].Trim();

				if (Key.Equals("Layout", StringComparison.OrdinalIgnoreCase))
				{
					Layout = Value;
				}
				else if (Key.Equals("Size", StringComparison.OrdinalIgnoreCase))
				{
					Size = Value;
				}
				else if (Key.Equals("Frame", StringComparison.OrdinalIgnoreCase))
				{
					Frame = Value;
				}
				else if (Key.Equals("Alt", StringComparison.OrdinalIgnoreCase))
				{
					Alt = string.IsNullOrWhiteSpace(Value) ? null : Value;
				}
				else if (Key.Equals("Caption", StringComparison.OrdinalIgnoreCase))
				{
					Caption = string.IsNullOrWhiteSpace(Value) ? null : Value;
				}
				else if (Key.Equals("CaptionPosition", StringComparison.OrdinalIgnoreCase))
				{
					CaptionPosition = Value;
				}
				else if (Key.Equals("Credit", StringComparison.OrdinalIgnoreCase))
				{
					Credit = string.IsNullOrWhiteSpace(Value) ? null : Value;
				}
				else if (Key.Equals("Link", StringComparison.OrdinalIgnoreCase))
				{
					Link = Value;
				}
				else
				{
					UnknownProperties.Add(Key);
				}
			}
		}

		Directive = new ParsedImageDirective(
			UrlSlug,
			Layout,
			Size,
			Frame,
			Alt,
			Caption,
			CaptionPosition,
			Credit,
			Link,
			UnknownProperties);
		return true;
	}

	/// <summary>Normalizes the layout token, defaulting to <c>center</c> when omitted or invalid.</summary>
	/// <param name="RawLayout">The authored <c>Layout=</c> value.</param>
	/// <returns>The normalized lowercase layout token.</returns>
	internal static string ResolveLayout(string? RawLayout)
		=> !string.IsNullOrWhiteSpace(RawLayout) && AllowedLayouts.Contains(RawLayout.Trim())
			? RawLayout.Trim().ToLowerInvariant()
			: "center";

	/// <summary>Normalizes the size token when valid for the resolved layout.</summary>
	/// <param name="RawSize">The authored <c>Size=</c> value.</param>
	/// <param name="ResolvedLayout">The normalized layout token.</param>
	/// <returns>The normalized lowercase size token, <c>medium</c> for <c>aside</c>/<c>start</c> when omitted, or <see langword="null"/>.</returns>
	internal static string? ResolveSize(string? RawSize, string ResolvedLayout)
	{
		if (ResolvedLayout is "full" or "breakout" or "max")
		{
			return null;
		}

		if (!string.IsNullOrWhiteSpace(RawSize) && AllowedSizes.Contains(RawSize.Trim()))
		{
			return RawSize.Trim().ToLowerInvariant();
		}

		return ResolvedLayout is "aside" or "start" ? "medium" : null;
	}

	/// <summary>Normalizes the frame token, defaulting to <c>none</c> when omitted or invalid.</summary>
	/// <param name="RawFrame">The authored <c>Frame=</c> value.</param>
	/// <returns>The normalized lowercase frame token.</returns>
	internal static string ResolveFrame(string? RawFrame)
		=> !string.IsNullOrWhiteSpace(RawFrame) && AllowedFrames.Contains(RawFrame.Trim())
			? RawFrame.Trim().ToLowerInvariant()
			: "none";

	/// <summary>Normalizes the link token, defaulting to <c>file</c> when omitted or invalid.</summary>
	/// <param name="RawLink">The authored <c>Link=</c> value.</param>
	/// <returns>The normalized lowercase link token (<c>file</c> or <c>none</c>).</returns>
	internal static string ResolveLink(string? RawLink)
		=> !string.IsNullOrWhiteSpace(RawLink) && AllowedLinks.Contains(RawLink.Trim())
			? RawLink.Trim().ToLowerInvariant()
			: "file";

	/// <summary>Normalizes the caption position token, defaulting to <c>bottom</c> when omitted or invalid.</summary>
	/// <param name="RawCaptionPosition">The authored <c>CaptionPosition=</c> value.</param>
	/// <returns>The normalized lowercase caption position (<c>bottom</c> or <c>top</c>).</returns>
	internal static string ResolveCaptionPosition(string? RawCaptionPosition)
		=> !string.IsNullOrWhiteSpace(RawCaptionPosition) && AllowedCaptionPositions.Contains(RawCaptionPosition.Trim())
			? RawCaptionPosition.Trim().ToLowerInvariant()
			: "bottom";

	/// <summary>Checks whether a caption position value is valid.</summary>
	/// <param name="Value">The authored caption position value.</param>
	/// <returns><see langword="true"/> when the value is recognized; otherwise, <see langword="false"/>.</returns>
	internal static bool IsValidCaptionPosition(string Value) => AllowedCaptionPositions.Contains(Value.Trim());

	/// <summary>Checks whether a layout value is valid.</summary>
	/// <param name="Value">The authored layout value.</param>
	/// <returns><see langword="true"/> when the value is recognized; otherwise, <see langword="false"/>.</returns>
	internal static bool IsValidLayout(string Value) => AllowedLayouts.Contains(Value.Trim());

	/// <summary>Checks whether a size value is valid.</summary>
	/// <param name="Value">The authored size value.</param>
	/// <returns><see langword="true"/> when the value is recognized; otherwise, <see langword="false"/>.</returns>
	internal static bool IsValidSize(string Value) => AllowedSizes.Contains(Value.Trim());

	/// <summary>Checks whether a frame value is valid.</summary>
	/// <param name="Value">The authored frame value.</param>
	/// <returns><see langword="true"/> when the value is recognized; otherwise, <see langword="false"/>.</returns>
	internal static bool IsValidFrame(string Value) => AllowedFrames.Contains(Value.Trim());

	/// <summary>Checks whether a link value is valid.</summary>
	/// <param name="Value">The authored link value.</param>
	/// <returns><see langword="true"/> when the value is recognized; otherwise, <see langword="false"/>.</returns>
	internal static bool IsValidLink(string Value) => AllowedLinks.Contains(Value.Trim());

	/// <summary>
	/// Matches a bare <c>|</c> separator only when followed by a recognized image property assignment,
	/// preserving legacy syntax without splitting literal pipes inside captions or credits.
	/// </summary>
	[GeneratedRegex(@"\|(?=\s*(?:Layout|Size|Frame|Alt|CaptionPosition|Caption|Credit|Link|Type)\s*=)", RegexOptions.IgnoreCase)]
	private static partial Regex LegacyPropertySeparatorRegex();
}

/// <summary>
/// Parses standalone <c>{{Image ...}}</c> lines as block figures so they are never wrapped in <c>&lt;p&gt;</c> tags.
/// </summary>
public sealed class ImageBlockParser : BlockParser
{
	/// <summary>Initializes a new instance of the <see cref="ImageBlockParser"/> class.</summary>
	public ImageBlockParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		ArgumentNullException.ThrowIfNull(Processor);
		if (Processor.IsCodeIndent)
		{
			return BlockState.None;
		}

		StringSlice Slice = Processor.Line;
		if (!Slice.Match(ImageDirectiveParser.MarkerStart))
		{
			return BlockState.None;
		}

		string LineText = Slice.Text[Slice.Start..(Slice.End + 1)];
		int CloseIndex = DirectiveSyntax.FindClosingDelimiter(LineText, ImageDirectiveParser.MarkerStart.Length);
		if (CloseIndex < 0 || !string.IsNullOrWhiteSpace(LineText[(CloseIndex + 2)..]))
		{
			return BlockState.None;
		}

		string Payload = LineText[ImageDirectiveParser.MarkerStart.Length..CloseIndex];
		if (!ImageDirectiveParser.TryParsePayload(Payload, out ParsedImageDirective? Parsed))
		{
			return BlockState.None;
		}

		ImageBlock Block = new(this)
		{
			Line = Processor.LineIndex,
			Column = Processor.Column,
			UrlSlug = Parsed.UrlSlug,
			Layout = Parsed.Layout,
			Size = Parsed.Size,
			Frame = Parsed.Frame,
			Alt = Parsed.Alt,
			Caption = Parsed.Caption,
			CaptionPosition = Parsed.CaptionPosition,
			Credit = Parsed.Credit,
			Link = Parsed.Link,
			UnknownProperties = Parsed.UnknownProperties
		};

		Processor.NewBlocks.Push(Block);
		Processor.Line.Start = Processor.Line.End + 1;
		return BlockState.BreakDiscard;
	}
}

/// <summary>
/// Parses the <c>{{Image ...}}</c> inline syntax when an image directive appears inside prose.
/// </summary>
public sealed class ImageInlineParser : InlineParser
{
	/// <summary>Initializes a new instance of the <see cref="ImageInlineParser"/> class.</summary>
	public ImageInlineParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override bool Match(InlineProcessor Processor, ref StringSlice Slice)
	{
		ArgumentNullException.ThrowIfNull(Processor);

		if (!Slice.Match(ImageDirectiveParser.MarkerStart))
		{
			return false;
		}

		int PayloadStart = Slice.Start + ImageDirectiveParser.MarkerStart.Length;
		int EndPos = DirectiveSyntax.FindClosingDelimiter(Slice.Text, PayloadStart);
		if (EndPos < 0 || EndPos + 1 > Slice.End)
		{
			return false;
		}

		string Payload = Slice.Text[PayloadStart..EndPos];
		if (!ImageDirectiveParser.TryParsePayload(Payload, out ParsedImageDirective? Parsed))
		{
			return false;
		}

		int InlineStart = Processor.GetSourcePosition(Slice.Start, out int Line, out int Column);
		Processor.Inline = new ImageInline
		{
			Span = new SourceSpan(InlineStart, InlineStart + (EndPos + 1 - Slice.Start)),
			Line = Line,
			Column = Column,
			UrlSlug = Parsed.UrlSlug,
			Layout = Parsed.Layout,
			Size = Parsed.Size,
			Frame = Parsed.Frame,
			Alt = Parsed.Alt,
			Caption = Parsed.Caption,
			CaptionPosition = Parsed.CaptionPosition,
			Credit = Parsed.Credit,
			Link = Parsed.Link,
			UnknownProperties = Parsed.UnknownProperties
		};

		Slice.Start = EndPos + 2;
		return true;
	}
}

/// <summary>
/// Shared rendering helpers for <see cref="ImageBlockRenderer"/> and <see cref="ImageInlineRenderer"/>.
/// </summary>
internal static class ImageRenderHelper
{
	/// <summary>Writes the missing-image fallback <c>&lt;img&gt;</c> and diagnostic comment.</summary>
	/// <param name="Renderer">The HTML renderer.</param>
	/// <param name="Node">The unresolved image directive node.</param>
	internal static void WriteMissingImage(HtmlRenderer Renderer, IImageDirectiveNode Node)
	{
		if (Node.CachedArticle is null)
		{
			Renderer.Write($"<!-- Article not found for UrlSlug: {WebUtility.HtmlEncode(Node.UrlSlug)} -->");
		}
		else if (Node.CachedArticle.CanonicalFileId is null)
		{
			Renderer.Write($"<!-- Article has no CanonicalFileId: {WebUtility.HtmlEncode(Node.UrlSlug)} -->");
		}
		else
		{
			Renderer.Write($"<!-- File not found for CanonicalFileId: {Node.CachedArticle.CanonicalFileId} -->");
		}

		Renderer.Write($"<img src=\"/sitefiles/{Node.SiteId.ToString(CultureInfo.InvariantCulture)}/missing-image.png\" alt=\"{WebUtility.HtmlEncode(SharedResource.Text("Missing Image"))}\" loading=\"lazy\" decoding=\"async\" />");
	}

	/// <summary>Writes the resolved <c>&lt;img&gt;</c> tag, optionally wrapped in a link to the file article.</summary>
	/// <param name="Renderer">The HTML renderer.</param>
	/// <param name="Node">The resolved image directive node.</param>
	/// <param name="Article">The resolved file article.</param>
	/// <param name="File">The resolved current file revision.</param>
	/// <param name="ExtraCssClass">Optional CSS class for inline compatibility.</param>
	internal static void WriteResolvedImage(
		HtmlRenderer Renderer,
		IImageDirectiveNode Node,
		ArticleRevision Article,
		FileRevision File,
		string? ExtraCssClass = null)
	{
		string ImageUrl = ImageUrlHelper.BuildImageUrl(Node.SiteId, File.CanonicalFileId, File.Id, File.Filename);
		string AltText = Node.Alt ?? Article.Title;
		string LinkMode = ImageDirectiveParser.ResolveLink(Node.Link);

		if (LinkMode != "none")
		{
			string FileArticlePath = ArticleUrlHelper.BuildArticlePath(Article);
			Renderer.Write($"<a class=\"article-image__link\" href=\"{WebUtility.HtmlEncode(FileArticlePath)}\">");
		}

		Renderer.Write($"<img src=\"{WebUtility.HtmlEncode(ImageUrl)}\" alt=\"{WebUtility.HtmlEncode(AltText)}\"");
		if (File.ImageWidthPixels is > 0 && File.ImageHeightPixels is > 0)
		{
			Renderer.Write($" width=\"{File.ImageWidthPixels.Value.ToString(CultureInfo.InvariantCulture)}\"");
			Renderer.Write($" height=\"{File.ImageHeightPixels.Value.ToString(CultureInfo.InvariantCulture)}\"");
		}
		if (!string.IsNullOrEmpty(ExtraCssClass))
		{
			Renderer.Write($" class=\"{ExtraCssClass}\"");
		}
		Renderer.Write(" loading=\"lazy\" decoding=\"async\" />");

		if (LinkMode != "none")
		{
			Renderer.Write("</a>");
		}
	}

	/// <summary>Renders short inline Markdown (emphasis and links) safely without wrapping in <c>&lt;p&gt;</c>.</summary>
	/// <param name="Renderer">The outer HTML renderer.</param>
	/// <param name="MarkdownText">The authored caption or credit text.</param>
	/// <param name="InlinePipeline">The restricted inline Markdown pipeline.</param>
	internal static void WriteInlineMarkdown(HtmlRenderer Renderer, string MarkdownText, MarkdownPipeline InlinePipeline)
	{
		string Normalized = MarkdownText.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
		MarkdownDocument Document = Markdown.Parse(Normalized, InlinePipeline);
		if (Document.Count == 1 && Document[0] is ParagraphBlock Paragraph && Paragraph.Inline is not null)
		{
			Renderer.WriteChildren(Paragraph.Inline);
		}
		else
		{
			Renderer.WriteEscape(Normalized);
		}
	}
}

/// <summary>
/// Renders a standalone <see cref="ImageBlock"/> as a semantic <c>&lt;figure class="article-image ..."&gt;</c>.
/// </summary>
/// <param name="InlinePipeline">The restricted inline Markdown pipeline for captions and credits.</param>
public sealed class ImageBlockRenderer(MarkdownPipeline InlinePipeline) : HtmlObjectRenderer<ImageBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, ImageBlock Block)
	{
		ArgumentNullException.ThrowIfNull(Renderer);
		ArgumentNullException.ThrowIfNull(Block);

		string Layout = ImageDirectiveParser.ResolveLayout(Block.Layout);
		string? Size = ImageDirectiveParser.ResolveSize(Block.Size, Layout);
		string Frame = ImageDirectiveParser.ResolveFrame(Block.Frame);
		bool HasCaption = !string.IsNullOrWhiteSpace(Block.Caption);
		bool HasCredit = !string.IsNullOrWhiteSpace(Block.Credit);
		bool CaptionOnTop = HasCaption && ImageDirectiveParser.ResolveCaptionPosition(Block.CaptionPosition) == "top";

		Renderer.Write($"<figure class=\"article-image article-image--{Layout}");
		if (Size is not null)
		{
			Renderer.Write($" article-image--{Size}");
		}
		if (Frame != "none")
		{
			Renderer.Write($" article-image--frame-{Frame}");
		}
		if (CaptionOnTop)
		{
			Renderer.Write(" article-image--caption-top");
		}
		Renderer.Write("\">");

		if (CaptionOnTop)
		{
			Renderer.Write("<figcaption class=\"article-image__caption article-image__caption--top\">");
			Renderer.Write("<span class=\"article-image__caption-text\">");
			ImageRenderHelper.WriteInlineMarkdown(Renderer, Block.Caption!, InlinePipeline);
			Renderer.Write("</span></figcaption>");
		}

		Renderer.Write("<div class=\"article-image__media\">");
		if (Block.CachedArticle is { CanonicalFileId: not null } Article && Block.CachedFile is { } File)
		{
			ImageRenderHelper.WriteResolvedImage(Renderer, Block, Article, File);
		}
		else
		{
			ImageRenderHelper.WriteMissingImage(Renderer, Block);
		}
		Renderer.Write("</div>");

		if (CaptionOnTop)
		{
			if (HasCredit)
			{
				Renderer.Write("<div class=\"article-image__footer\"><span class=\"article-image__credit\">");
				ImageRenderHelper.WriteInlineMarkdown(Renderer, Block.Credit!, InlinePipeline);
				Renderer.Write("</span></div>");
			}
		}
		else if (HasCaption || HasCredit)
		{
			Renderer.Write("<figcaption class=\"article-image__caption\">");
			if (HasCaption)
			{
				Renderer.Write("<span class=\"article-image__caption-text\">");
				ImageRenderHelper.WriteInlineMarkdown(Renderer, Block.Caption!, InlinePipeline);
				Renderer.Write("</span>");
			}
			if (HasCredit)
			{
				Renderer.Write("<span class=\"article-image__credit\">");
				ImageRenderHelper.WriteInlineMarkdown(Renderer, Block.Credit!, InlinePipeline);
				Renderer.Write("</span>");
			}
			Renderer.Write("</figcaption>");
		}

		Renderer.WriteLine("</figure>");
	}
}

/// <summary>
/// Renders an inline <see cref="ImageInline"/> within prose without emitting block <c>&lt;figure&gt;</c> tags.
/// </summary>
public sealed class ImageInlineRenderer : HtmlObjectRenderer<ImageInline>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, ImageInline InlineElement)
	{
		ArgumentNullException.ThrowIfNull(Renderer);
		ArgumentNullException.ThrowIfNull(InlineElement);

		if (InlineElement.CachedArticle is not { CanonicalFileId: not null } Article || InlineElement.CachedFile is not { } File)
		{
			ImageRenderHelper.WriteMissingImage(Renderer, InlineElement);
			return;
		}

		string? CssClass = InlineElement.Layout?.Trim().ToLowerInvariant() switch
		{
			"full" => "full",
			"breakout" => "breakout",
			"max" => "max",
			_ => null
		};

		ImageRenderHelper.WriteResolvedImage(Renderer, InlineElement, Article, File, CssClass);
	}
}

/// <summary>
/// A Markdig extension that adds support for embedding wiki-hosted <c>{{Image ...}}</c> figures and inline images.
/// </summary>
/// <param name="ApprovedDomains">External domains covered by the site's approved-link policy for captions and credits.</param>
public sealed class ImageExtension(IReadOnlyList<string>? ApprovedDomains = null) : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		ArgumentNullException.ThrowIfNull(Pipeline);

		if (!Pipeline.BlockParsers.Contains<ImageBlockParser>())
		{
			Pipeline.BlockParsers.Insert(0, new ImageBlockParser());
		}

		if (!Pipeline.InlineParsers.Contains<ImageInlineParser>())
		{
			Pipeline.InlineParsers.Add(new ImageInlineParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		ArgumentNullException.ThrowIfNull(Pipeline);
		ArgumentNullException.ThrowIfNull(Renderer);

		if (Renderer is HtmlRenderer HtmlRenderer)
		{
			if (!HtmlRenderer.ObjectRenderers.Contains<ImageBlockRenderer>())
			{
				MarkdownPipeline CaptionPipeline = new MarkdownPipelineBuilder()
					.UseEmphasisExtras()
					.Use(new BareWwwTextExtension())
					.Use(new MarkdownSafetyExtension())
					.Use(new ApprovedNoFollowLinksExtension(ApprovedDomains ?? []))
					.DisableHtml()
					.Build();
				HtmlRenderer.ObjectRenderers.Insert(0, new ImageBlockRenderer(CaptionPipeline));
			}

			if (!HtmlRenderer.ObjectRenderers.Contains<ImageInlineRenderer>())
			{
				HtmlRenderer.ObjectRenderers.Add(new ImageInlineRenderer());
			}
		}
	}

	/// <summary>
	/// Asynchronously resolves all <c>{{Image ...}}</c> block and inline references in the document to file URLs.
	/// </summary>
	/// <param name="Document">The markdown document.</param>
	/// <param name="Context">The database context.</param>
	/// <param name="SiteId">The current site ID.</param>
	/// <param name="Culture">The culture code.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	public static async Task EnrichAsync(
		MarkdownDocument Document,
		WikiWikiWorldDbContext Context,
		int SiteId,
		string Culture,
		CancellationToken CancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(Document);
		ArgumentNullException.ThrowIfNull(Context);

		List<IImageDirectiveNode> ImageNodes = [.. Document.Descendants().OfType<IImageDirectiveNode>()];
		if (ImageNodes.Count == 0)
		{
			return;
		}

		List<string> Slugs = [.. ImageNodes
			.Select(Node => ArticleUrlHelper.NormalizeLookupSlug(Node.UrlSlug.Trim()))
			.Distinct(StringComparer.OrdinalIgnoreCase)];

		List<ArticleRevision> Articles = await Context.ArticleRevisions
			.AsNoTracking()
			.Where(x => x.SiteId == SiteId && x.Culture == Culture && Slugs.Contains(x.UrlSlug) && x.IsCurrent && x.DateDeleted == null)
			.ToListAsync(CancellationToken);

		Dictionary<string, ArticleRevision> ArticleLookup = Articles.ToDictionary(a => a.UrlSlug, StringComparer.OrdinalIgnoreCase);

		List<Guid> FileIds = [.. Articles
			.Where(a => a.CanonicalFileId.HasValue)
			.Select(a => a.CanonicalFileId!.Value)
			.Distinct()];

		List<FileRevision> Files = FileIds.Count > 0
			? await Context.FileRevisions
				.AsNoTracking()
				.Where(f => FileIds.Contains(f.CanonicalFileId) && f.IsCurrent == true && f.DateDeleted == null)
				.ToListAsync(CancellationToken)
			: [];

		Dictionary<Guid, FileRevision> FileLookup = Files.ToDictionary(f => f.CanonicalFileId);

		foreach (IImageDirectiveNode Node in ImageNodes)
		{
			Node.SiteId = SiteId;
			string Slug = ArticleUrlHelper.NormalizeLookupSlug(Node.UrlSlug.Trim());
			if (ArticleLookup.TryGetValue(Slug, out ArticleRevision? Article))
			{
				Node.CachedArticle = Article;
				if (Article.CanonicalFileId.HasValue && FileLookup.TryGetValue(Article.CanonicalFileId.Value, out FileRevision? File))
				{
					Node.CachedFile = File;
				}
			}
		}
	}

	/// <summary>
	/// Lists save warnings for <c>{{Image ...}}</c> directives with unknown properties or unsupported option values.
	/// </summary>
	/// <param name="Document">The parsed article document.</param>
	/// <returns>One plain-English warning per problem in document order; empty when valid.</returns>
	public static IReadOnlyList<string> FindProblems(MarkdownDocument Document)
	{
		ArgumentNullException.ThrowIfNull(Document);

		List<string> Problems = [];
		foreach (IImageDirectiveNode Node in Document.Descendants().OfType<IImageDirectiveNode>())
		{
			int LineNumber = Node.Line + 1;

			foreach (string Unknown in Node.UnknownProperties)
			{
				Problems.Add($"Line {LineNumber}: {{{{Image}}}} has an unknown property \"{Unknown}\". "
					+ "Supported properties are Layout, Size, Frame, Alt, Caption, CaptionPosition, Credit, and Link.");
			}

			if (Node.Layout is not null && !ImageDirectiveParser.IsValidLayout(Node.Layout))
			{
				Problems.Add($"Line {LineNumber}: {{{{Image}}}} Layout=\"{Node.Layout}\" is not supported. "
					+ "Use center, aside, start, full, breakout, or max.");
			}

			if (Node.Size is not null)
			{
				if (!ImageDirectiveParser.IsValidSize(Node.Size))
				{
					Problems.Add($"Line {LineNumber}: {{{{Image}}}} Size=\"{Node.Size}\" is not supported. "
						+ "Use small, medium, or large.");
				}
				else
				{
					string ResolvedLayout = ImageDirectiveParser.ResolveLayout(Node.Layout);
					if (ResolvedLayout is "full" or "breakout" or "max")
					{
						Problems.Add($"Line {LineNumber}: {{{{Image}}}} Size=\"{Node.Size}\" has no effect with Layout={ResolvedLayout}. "
							+ "Size only applies to center, aside, and start layouts.");
					}
				}
			}

			if (Node.Frame is not null && !ImageDirectiveParser.IsValidFrame(Node.Frame))
			{
				Problems.Add($"Line {LineNumber}: {{{{Image}}}} Frame=\"{Node.Frame}\" is not supported. "
					+ "Use none, border, paper, or dark.");
			}

			if (Node.CaptionPosition is not null)
			{
				if (!ImageDirectiveParser.IsValidCaptionPosition(Node.CaptionPosition))
				{
					Problems.Add($"Line {LineNumber}: {{{{Image}}}} CaptionPosition=\"{Node.CaptionPosition}\" is not supported. "
						+ "Use bottom or top.");
				}
				else if (ImageDirectiveParser.ResolveCaptionPosition(Node.CaptionPosition) == "top" &&
					string.IsNullOrWhiteSpace(Node.Caption))
				{
					Problems.Add($"Line {LineNumber}: {{{{Image}}}} CaptionPosition=\"{Node.CaptionPosition}\" has no effect without Caption.");
				}
			}

			if (Node.Link is not null && !ImageDirectiveParser.IsValidLink(Node.Link))
			{
				Problems.Add($"Line {LineNumber}: {{{{Image}}}} Link=\"{Node.Link}\" is not supported. "
					+ "Use file or none.");
			}
		}

		return Problems;
	}
}
