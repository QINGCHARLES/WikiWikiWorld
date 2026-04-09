using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A Markdig extension that extracts image provenance/source citation from the document.
/// Authors add <c>{{Source description |#| url}}</c> to cite the origin of a file.
/// </summary>
public sealed class SourceExtension : IMarkdownExtension
{
	/// <summary>
	/// The document metadata key used to store the source display text.
	/// </summary>
	public const string TextKey = "SourceText";

	/// <summary>
	/// The document metadata key used to store the optional source URL.
	/// </summary>
	public const string UrlKey = "SourceUrl";

	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<SourceBlockParser>())
			Pipeline.BlockParsers.Add(new SourceBlockParser());
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRendererInstance &&
			!HtmlRendererInstance.ObjectRenderers.Contains<SourceRenderer>())
		{
			HtmlRendererInstance.ObjectRenderers.Add(new SourceRenderer());
		}
	}

	/// <summary>
	/// Extracts the source text and optional URL from the parsed document and stores them in metadata.
	/// Only the first <see cref="SourceBlock"/> is used; duplicates are silently ignored.
	/// </summary>
	/// <param name="Document">The parsed markdown document.</param>
	public static void Enrich(MarkdownDocument Document)
	{
		SourceBlock? Block = Document.Descendants<SourceBlock>().FirstOrDefault();
		if (Block is null)
			return;

		Document.SetData(TextKey, Block.SourceText);

		if (Block.SourceUrl is not null)
			Document.SetData(UrlKey, Block.SourceUrl);
	}
}

/// <summary>
/// Parses the <c>{{Source ...}}</c> block syntax.
/// Supports three variants: text-only, URL-only, and text + URL separated by <c>|#|</c>.
/// </summary>
public sealed class SourceBlockParser : BlockParser
{
	private const string MarkerStart = "{{Source ";
	private const string MarkerEnd = "}}";
	private const string AttributeSeparator = "|#|";

	/// <summary>
	/// Initializes a new instance of the <see cref="SourceBlockParser"/> class.
	/// </summary>
	public SourceBlockParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		if (!Processor.Line.Match(MarkerStart))
			return BlockState.None;

		StringSlice Line = Processor.Line;
		int ContentStart = Line.Start + MarkerStart.Length;
		int EndPos = Line.Text.IndexOf(MarkerEnd, ContentStart, StringComparison.Ordinal);

		if (EndPos == -1)
			return BlockState.None;

		string RawContent = Line.Text.Substring(ContentStart, EndPos - ContentStart).Trim();

		if (RawContent.Length == 0)
			return BlockState.None;

		string SourceText;
		string? SourceUrl = null;

		int SeparatorPos = RawContent.IndexOf(AttributeSeparator, StringComparison.Ordinal);

		if (SeparatorPos >= 0)
		{
			// Text + URL variant: "Description |#| https://example.com"
			SourceText = RawContent[..SeparatorPos].Trim();
			string UrlPart = RawContent[(SeparatorPos + AttributeSeparator.Length)..].Trim();

			if (UrlPart.Length > 0)
				SourceUrl = UrlPart;

			// If the text portion was empty but we have a URL, use the URL as display text
			if (SourceText.Length == 0 && SourceUrl is not null)
				SourceText = SourceUrl;
		}
		else if (RawContent.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
				 RawContent.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			// URL-only variant: the entire content is a URL
			SourceText = RawContent;
			SourceUrl = RawContent;
		}
		else
		{
			// Text-only variant
			SourceText = RawContent;
		}

		Processor.Line.Start = EndPos + MarkerEnd.Length;

		SourceBlock Block = new(this)
		{
			SourceText = SourceText,
			SourceUrl = SourceUrl
		};

		Processor.NewBlocks.Push(Block);
		return BlockState.BreakDiscard;
	}
}

/// <summary>
/// A leaf block representing a source citation in a markdown document.
/// </summary>
public sealed class SourceBlock : LeafBlock
{
	/// <summary>
	/// Gets or sets the display text of the source citation.
	/// </summary>
	public string SourceText { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the optional URL of the source citation.
	/// </summary>
	public string? SourceUrl { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="SourceBlock"/> class.
	/// </summary>
	/// <param name="Parser">The block parser that created this block.</param>
	public SourceBlock(BlockParser Parser) : base(Parser)
	{
		ProcessInlines = false;
	}
}

/// <summary>
/// Renders a <see cref="SourceBlock"/> as a hidden HTML comment (metadata only, no visible output).
/// </summary>
public sealed class SourceRenderer : HtmlObjectRenderer<SourceBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, SourceBlock Block)
	{
		// Metadata only, no visible output
		if (Renderer.EnableHtmlForBlock)
		{
			Renderer.Write($"<!-- Source: {Block.SourceText} -->");
		}
	}
}
