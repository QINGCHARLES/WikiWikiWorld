using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// Renders <see cref="HeadingBlock"/> elements and appends a section anchor link to top-level <c>h2</c> headings
/// when section anchors are enabled on the <see cref="MarkdownDocument"/>.
/// </summary>
public sealed class SectionHeadingRenderer : HtmlObjectRenderer<HeadingBlock>
{
	private static readonly string[] HeadingTags = ["h1", "h2", "h3", "h4", "h5", "h6"];

	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, HeadingBlock Block)
	{
		ArgumentNullException.ThrowIfNull(Renderer);
		ArgumentNullException.ThrowIfNull(Block);

		int Index = Block.Level - 1;
		string HeadingTag = (uint)Index < (uint)HeadingTags.Length
			? HeadingTags[Index]
			: $"h{Block.Level.ToString(CultureInfo.InvariantCulture)}";

		if (Renderer.EnableHtmlForBlock)
		{
			Renderer.Write('<');
			Renderer.Write(HeadingTag);
			Renderer.WriteAttributes(Block);
			Renderer.Write('>');
		}

		Renderer.WriteLeafInline(Block);

		if (Renderer.EnableHtmlForBlock)
		{
			if (Block.Level == 2
				&& Block.Parent is MarkdownDocument Document
				&& HeadingAnchorExtension.IsEnabled(Document)
				&& Block.TryGetAttributes()?.Id is { } HeadingId
				&& !string.IsNullOrWhiteSpace(HeadingId))
			{
				string SectionTitle = HeadingAnchorExtension.ExtractPlainText(Block.Inline);
				HeadingAnchorExtension.WriteAnchor(Renderer, HeadingId, SectionTitle);
			}

			Renderer.Write("</");
			Renderer.Write(HeadingTag);
			Renderer.WriteLine('>');
		}
	}
}

/// <summary>
/// Coordinates section heading IDs and trailing anchor links across authored <c>h2</c> headings and generated
/// reference sections (<c>{{Footnotes}}</c> and <c>{{Citations}}</c>).
/// </summary>
public sealed class HeadingAnchorExtension : IMarkdownExtension
{
	/// <summary>
	/// Document metadata key indicating that full-article section anchor links should be emitted.
	/// </summary>
	public const string EnabledDocumentKey = "HeadingAnchorsEnabled";

	private const string ReservedIdsDocumentKey = "HeadingAnchorsReservedIds";

	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer)
		{
			HtmlRenderer.ObjectRenderers.TryRemove<HeadingRenderer>();
			HtmlRenderer.ObjectRenderers.Add(new SectionHeadingRenderer());
		}
	}

	/// <summary>
	/// Marks a parsed <see cref="MarkdownDocument"/> so top-level <c>h2</c> headings and generated reference headings
	/// emit trailing section anchor links during HTML rendering.
	/// </summary>
	/// <param name="Document">The parsed article document.</param>
	public static void Enrich(MarkdownDocument Document)
	{
		ArgumentNullException.ThrowIfNull(Document);
		Document.SetData(EnabledDocumentKey, true);
	}

	/// <summary>
	/// Determines whether trailing section anchor links are enabled for <paramref name="Document"/>.
	/// </summary>
	/// <param name="Document">The owning Markdown document, if available.</param>
	/// <returns><see langword="true"/> when <see cref="Enrich"/> was called on the document; otherwise <see langword="false"/>.</returns>
	public static bool IsEnabled(MarkdownDocument? Document) =>
		Document?.GetData(EnabledDocumentKey) is true;

	/// <summary>
	/// Reserves a unique HTML <c>id</c> on <paramref name="Document"/> for a generated section heading, appending
	/// <c>-1</c>, <c>-2</c>, and so on if an authored element or earlier block already claimed <paramref name="PreferredId"/>.
	/// </summary>
	/// <param name="Document">The parsed Markdown document whose existing element IDs are inspected.</param>
	/// <param name="PreferredId">The canonical base identifier (such as <c>footnotes</c> or <c>citations</c>).</param>
	/// <returns>The unique identifier reserved for the caller.</returns>
	public static string ReserveId(MarkdownDocument Document, string PreferredId)
	{
		ArgumentNullException.ThrowIfNull(Document);
		ArgumentException.ThrowIfNullOrWhiteSpace(PreferredId);

		if (Document.GetData(ReservedIdsDocumentKey) is not HashSet<string> TakenIds)
		{
			TakenIds = CollectExistingIds(Document);
			Document.SetData(ReservedIdsDocumentKey, TakenIds);
		}

		if (TakenIds.Add(PreferredId))
		{
			return PreferredId;
		}

		int Suffix = 1;
		while (true)
		{
			string Candidate = $"{PreferredId}-{Suffix.ToString(CultureInfo.InvariantCulture)}";
			if (TakenIds.Add(Candidate))
			{
				return Candidate;
			}
			Suffix++;
		}
	}

	/// <summary>
	/// Writes a generated <c>h2</c> heading with its <c>id</c> attribute and, when enabled on the owning document,
	/// a trailing section anchor link.
	/// </summary>
	/// <param name="Renderer">The HTML renderer receiving the heading markup.</param>
	/// <param name="OwnerBlock">The AST block being rendered, used to locate the owning <see cref="MarkdownDocument"/>.</param>
	/// <param name="Id">The unique HTML identifier assigned to the heading.</param>
	/// <param name="HeadingText">The localized plain-text heading label.</param>
	public static void WriteGeneratedHeading(HtmlRenderer Renderer, Block OwnerBlock, string Id, string HeadingText)
	{
		ArgumentNullException.ThrowIfNull(Renderer);
		ArgumentNullException.ThrowIfNull(OwnerBlock);
		ArgumentException.ThrowIfNullOrWhiteSpace(Id);
		ArgumentNullException.ThrowIfNull(HeadingText);

		Renderer.Write("<h2 id=\"");
		Renderer.WriteEscape(Id);
		Renderer.Write("\">");
		Renderer.WriteEscape(HeadingText);
		if (IsEnabled(FindDocument(OwnerBlock)))
		{
			WriteAnchor(Renderer, Id, HeadingText);
		}
		Renderer.Write("</h2>");
	}

	/// <summary>
	/// Writes the trailing <c>&lt;a class="heading-anchor"&gt;</c> link targeting <paramref name="Id"/> with an accessible label.
	/// </summary>
	/// <param name="Renderer">The HTML renderer receiving the anchor element.</param>
	/// <param name="Id">The target element's <c>id</c> value without a leading <c>#</c>.</param>
	/// <param name="SectionTitle">The visible plain-text title of the section, if any.</param>
	public static void WriteAnchor(HtmlRenderer Renderer, string Id, string SectionTitle)
	{
		ArgumentNullException.ThrowIfNull(Renderer);
		ArgumentException.ThrowIfNullOrWhiteSpace(Id);

		string Fragment = Id
			.Replace("%", "%25", StringComparison.Ordinal)
			.Replace("#", "%23", StringComparison.Ordinal)
			.Replace("?", "%3F", StringComparison.Ordinal);

		string Label = string.IsNullOrWhiteSpace(SectionTitle)
			? SharedResource.Text("Link to section")
			: SharedResource.Format("Link to section: {0}", SectionTitle);

		Renderer.Write("<a class=\"heading-anchor\" href=\"#");
		Renderer.WriteEscape(Fragment);
		Renderer.Write("\" aria-label=\"");
		Renderer.WriteEscape(Label);
		Renderer.Write("\"></a>");
	}

	/// <summary>
	/// Extracts normalized visible plain text from a heading's inline tree for accessible labeling,
	/// omitting superscript citation, footnote, and category markers.
	/// </summary>
	/// <param name="Root">The heading's root inline container.</param>
	/// <returns>Trimmed plain text with internal whitespace runs collapsed to single spaces.</returns>
	internal static string ExtractPlainText(ContainerInline? Root)
	{
		if (Root is null)
		{
			return string.Empty;
		}

		StringBuilder Builder = new();
		AppendInlineText(Root, Builder);

		StringBuilder Normalized = new(Builder.Length);
		bool PendingSpace = false;
		foreach (char Ch in Builder.ToString())
		{
			if (char.IsWhiteSpace(Ch))
			{
				PendingSpace = Normalized.Length > 0;
				continue;
			}

			if (PendingSpace)
			{
				Normalized.Append(' ');
				PendingSpace = false;
			}
			Normalized.Append(Ch);
		}

		return Normalized.ToString();
	}

	/// <summary>
	/// Recursively appends visible text from <paramref name="Container"/> into <paramref name="Builder"/>.
	/// </summary>
	/// <param name="Container">The inline container to walk.</param>
	/// <param name="Builder">Receives raw visible text segments.</param>
	private static void AppendInlineText(ContainerInline Container, StringBuilder Builder)
	{
		Inline? Current = Container.FirstChild;
		while (Current is not null)
		{
			switch (Current)
			{
				case LiteralInline Literal:
					Builder.Append(Literal.Content.AsSpan());
					break;
				case CodeInline Code:
					Builder.Append(Code.Content);
					break;
				case LineBreakInline:
					Builder.Append(' ');
					break;
				case YouTubeLinkInline YouTubeLink:
					Builder.Append(YouTubeLink.Label);
					break;
				case CitationInline or FootnoteInline or CategoryInline:
					break;
				case ContainerInline Nested:
					AppendInlineText(Nested, Builder);
					break;
			}

			Current = Current.NextSibling;
		}
	}

	/// <summary>
	/// Walks upward from <paramref name="Node"/> to find its owning <see cref="MarkdownDocument"/>.
	/// </summary>
	/// <param name="Node">A block inside a parsed Markdown tree.</param>
	/// <returns>The root <see cref="MarkdownDocument"/>, or <see langword="null"/> if detached.</returns>
	private static MarkdownDocument? FindDocument(Block Node)
	{
		Block? Current = Node;
		while (Current is not null)
		{
			if (Current is MarkdownDocument Document)
			{
				return Document;
			}
			Current = Current.Parent;
		}

		return null;
	}

	/// <summary>
	/// Collects all explicit and auto-generated HTML <c>id</c> values already present on <paramref name="Document"/>.
	/// </summary>
	/// <param name="Document">The parsed Markdown document.</param>
	/// <returns>A case-sensitive set of existing element IDs.</returns>
	private static HashSet<string> CollectExistingIds(MarkdownDocument Document)
	{
		HashSet<string> Ids = new(StringComparer.Ordinal);
		foreach (MarkdownObject Node in Document.Descendants().Prepend(Document))
		{
			HtmlAttributes? Attributes = Node.TryGetAttributes();
			if (Attributes is null)
			{
				continue;
			}

			if (!string.IsNullOrWhiteSpace(Attributes.Id))
			{
				Ids.Add(Attributes.Id);
			}

			if (Attributes.Properties is null)
			{
				continue;
			}

			foreach (KeyValuePair<string, string?> Property in Attributes.Properties)
			{
				if (Property.Key.Equals("id", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(Property.Value))
				{
					Ids.Add(Property.Value);
				}
			}
		}

		return Ids;
	}
}
