using System.Collections.Frozen;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// The supported eyebrow types that map to display labels.
/// </summary>
public enum EyebrowType
{
	/// <summary>A generic magazine article.</summary>
	Magazine,

	/// <summary>A specific magazine issue.</summary>
	MagazineIssue,

	/// <summary>A franchise spanning related publications or media.</summary>
	Franchise,

	/// <summary>An independently produced zine publication.</summary>
	Zine,

	/// <summary>A scholarly, professional, or literary journal.</summary>
	Journal,

	/// <summary>A person associated with publications or their creation.</summary>
	Person,

	/// <summary>A company or organization that publishes publications.</summary>
	Publisher,

	/// <summary>An association, institution, or other organization.</summary>
	Organization,

	/// <summary>A specific issue of a zine publication.</summary>
	ZineIssue,

	/// <summary>A specific issue of a scholarly, professional, or literary journal.</summary>
	JournalIssue,

	/// <summary>A newsletter publication distributed to its subscribers or members.</summary>
	Newsletter,

	/// <summary>A specific issue of a newsletter publication.</summary>
	NewsletterIssue
}

/// <summary>
/// A Markdig extension that extracts an eyebrow label from the document.
/// Authors add <c>{{Eyebrow TYPE}}</c> to display a label between the header image and the title.
/// </summary>
public sealed class EyebrowExtension : IMarkdownExtension
{
	/// <summary>
	/// The document metadata key used to store the eyebrow display label.
	/// </summary>
	public const string DocumentKey = "Eyebrow";

	/// <summary>
	/// Maps each <see cref="EyebrowType"/> to its human-readable display label.
	/// </summary>
	private static readonly FrozenDictionary<EyebrowType, string> DisplayLabels = new Dictionary<EyebrowType, string>
	{
		[EyebrowType.Magazine] = "Magazine",
		[EyebrowType.MagazineIssue] = "Magazine Issue",
		[EyebrowType.Franchise] = "Franchise",
		[EyebrowType.Zine] = "Zine",
		[EyebrowType.Journal] = "Journal",
		[EyebrowType.Person] = "Person",
		[EyebrowType.Publisher] = "Publisher",
		[EyebrowType.Organization] = "Organization",
		[EyebrowType.ZineIssue] = "Zine Issue",
		[EyebrowType.JournalIssue] = "Journal Issue",
		[EyebrowType.Newsletter] = "Newsletter",
		[EyebrowType.NewsletterIssue] = "Newsletter Issue"
	}.ToFrozenDictionary();

	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<EyebrowBlockParser>())
		{
			Pipeline.BlockParsers.Add(new EyebrowBlockParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRendererInstance &&
			!HtmlRendererInstance.ObjectRenderers.Contains<EyebrowRenderer>())
		{
			HtmlRendererInstance.ObjectRenderers.Add(new EyebrowRenderer());
		}
	}

	/// <summary>
	/// Extracts the eyebrow type from the parsed document and stores the display label in metadata.
	/// Only the first valid <see cref="EyebrowBlock"/> is used; unknown types and duplicates are ignored.
	/// </summary>
	/// <param name="Document">The parsed markdown document.</param>
	public static void Enrich(MarkdownDocument Document)
	{
		EyebrowBlock? Block = Document.Descendants<EyebrowBlock>().FirstOrDefault(x => !x.IsMalformed);
		if (Block is not null && DisplayLabels.TryGetValue(Block.Type, out string? Label))
		{
			Document.SetData(DocumentKey, Label);
		}
	}
}

/// <summary>
/// Parses the <c>{{Eyebrow TYPE}}</c> block syntax.
/// Consumes unrecognised type values as malformed blocks with no output or label.
/// </summary>
public sealed class EyebrowBlockParser : BlockParser
{
	private const string MarkerStart = "{{Eyebrow ";
	private const string MarkerEnd = "}}";

	/// <summary>
	/// Maps raw type strings (case-insensitive) to <see cref="EyebrowType"/> enum values.
	/// </summary>
	private static readonly FrozenDictionary<string, EyebrowType> TypeLookup =
		new Dictionary<string, EyebrowType>(StringComparer.OrdinalIgnoreCase)
		{
			["MAGAZINE"] = EyebrowType.Magazine,
			["MAGAZINEISSUE"] = EyebrowType.MagazineIssue,
			["FRANCHISE"] = EyebrowType.Franchise,
			["ZINE"] = EyebrowType.Zine,
			["ZINEISSUE"] = EyebrowType.ZineIssue,
			["JOURNAL"] = EyebrowType.Journal,
			["JOURNALISSUE"] = EyebrowType.JournalIssue,
			["NEWSLETTER"] = EyebrowType.Newsletter,
			["NEWSLETTERISSUE"] = EyebrowType.NewsletterIssue,
			["PERSON"] = EyebrowType.Person,
			["PUBLISHER"] = EyebrowType.Publisher,
			["ORGANIZATION"] = EyebrowType.Organization
		}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Initializes a new instance of the <see cref="EyebrowBlockParser"/> class.
	/// </summary>
	public EyebrowBlockParser() => OpeningCharacters = ['{'];

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

		string RawType = Line.Text.Substring(ContentStart, EndPos - ContentStart).Trim();

		if (!TypeLookup.TryGetValue(RawType, out EyebrowType Type))
		{
			// Unrecognised type — consume the block silently (no output)
			Processor.Line.Start = EndPos + MarkerEnd.Length;
			Processor.NewBlocks.Push(new EyebrowBlock(this) { Type = Type, IsMalformed = true });
			return BlockState.BreakDiscard;
		}

		Processor.Line.Start = EndPos + MarkerEnd.Length;

		EyebrowBlock Block = new(this)
		{
			Type = Type
		};

		Processor.NewBlocks.Push(Block);
		return BlockState.BreakDiscard;
	}
}

/// <summary>
/// A leaf block representing an eyebrow label in a markdown document.
/// </summary>
public sealed class EyebrowBlock : LeafBlock
{
	/// <summary>
	/// Gets or sets the parsed eyebrow type.
	/// </summary>
	public required EyebrowType Type { get; init; }

	/// <summary>
	/// Gets or sets a value indicating whether the block had an unrecognised type string.
	/// </summary>
	public bool IsMalformed { get; init; }

	/// <summary>
	/// Initializes a new instance of the <see cref="EyebrowBlock"/> class.
	/// </summary>
	/// <param name="Parser">The block parser that created this block.</param>
	public EyebrowBlock(BlockParser Parser) : base(Parser)
	{
		ProcessInlines = false;
	}
}

/// <summary>
/// Renders an <see cref="EyebrowBlock"/> as a hidden HTML comment (metadata only, no visible output).
/// The actual eyebrow is rendered by the Razor view from document metadata.
/// </summary>
public sealed class EyebrowRenderer : HtmlObjectRenderer<EyebrowBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, EyebrowBlock Block)
	{
		if (Renderer.EnableHtmlForBlock && !Block.IsMalformed)
		{
			Renderer.Write($"<!-- Eyebrow: {Block.Type} -->");
		}
	}
}
