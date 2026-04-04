using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A Markdig extension that renders a stub notice banner at the end of articles.
/// Authors add <c>{{Stub}}</c> to signal the article is incomplete.
/// </summary>
public sealed class StubExtension : IMarkdownExtension
{
	/// <summary>
	/// The document metadata key used to store the site name for the stub banner.
	/// </summary>
	public const string SiteNameKey = "StubSiteName";

	/// <summary>
	/// The document metadata key used to store the article edit URL for the stub banner.
	/// </summary>
	public const string EditUrlKey = "StubEditUrl";

	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<StubBlockParser>())
			Pipeline.BlockParsers.Add(new StubBlockParser());
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRendererInstance &&
			!HtmlRendererInstance.ObjectRenderers.Contains<StubRenderer>())
		{
			HtmlRendererInstance.ObjectRenderers.Add(new StubRenderer());
		}
	}

	/// <summary>
	/// Stores the site name and edit URL in the document so the renderer can produce
	/// a properly linked stub banner.
	/// </summary>
	/// <param name="Document">The parsed markdown document.</param>
	/// <param name="SiteName">The display name of the current site.</param>
	/// <param name="EditUrl">The relative URL to edit the current article.</param>
	public static void Enrich(MarkdownDocument Document, string SiteName, string EditUrl)
	{
		Document.SetData(SiteNameKey, SiteName);
		Document.SetData(EditUrlKey, EditUrl);
	}
}

/// <summary>
/// Parses the <c>{{Stub}}</c> block marker from markdown source.
/// </summary>
public sealed class StubBlockParser : BlockParser
{
	private const string Marker = "{{Stub}}";

	/// <summary>
	/// Initializes a new instance of the <see cref="StubBlockParser"/> class.
	/// </summary>
	public StubBlockParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		if (!Processor.Line.Match(Marker))
			return BlockState.None;

		Processor.Line.Start += Marker.Length;
		Processor.NewBlocks.Push(new StubBlock(this));
		return BlockState.BreakDiscard;
	}
}

/// <summary>
/// A leaf block representing a stub notice in a markdown document.
/// </summary>
/// <param name="Parser">The block parser that created this block.</param>
public sealed class StubBlock(BlockParser Parser) : LeafBlock(Parser)
{
}

/// <summary>
/// Renders a <see cref="StubBlock"/> as an HTML stub-notice banner.
/// </summary>
public sealed class StubRenderer : HtmlObjectRenderer<StubBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, StubBlock Block)
	{
		if (!Renderer.EnableHtmlForBlock)
			return;

		// Traverse to the root document to retrieve enriched metadata
		Block Root = Block;
		while (Root.Parent is not null)
			Root = Root.Parent;

		if (Root is not MarkdownDocument Document)
			return;

		string SiteName = Document.GetData(StubExtension.SiteNameKey) as string ?? string.Empty;
		string EditUrl = Document.GetData(StubExtension.EditUrlKey) as string ?? string.Empty;

		Renderer.WriteLine("<div class=\"stub-banner\" role=\"note\">");
		Renderer.Write("  <span class=\"stub-banner__icon\" aria-hidden=\"true\"></span>");
		Renderer.Write("  <p>");
		Renderer.Write("This article is a ");
		Renderer.Write("<a href=\"/wiki:stub\">stub</a>");
		Renderer.Write(". You can improve ");

		if (!string.IsNullOrEmpty(SiteName))
		{
			Renderer.Write("<strong>");
			Renderer.WriteEscape(SiteName);
			Renderer.Write("</strong>");
		}
		else
		{
			Renderer.Write("this wiki");
		}

		Renderer.Write(" by ");

		if (!string.IsNullOrEmpty(EditUrl))
		{
			Renderer.Write("<a href=\"");
			Renderer.WriteEscapeUrl(EditUrl);
			Renderer.Write("\">");
		}

		Renderer.Write("adding missing information");

		if (!string.IsNullOrEmpty(EditUrl))
			Renderer.Write("</a>");

		Renderer.Write(".</p>");
		Renderer.WriteLine("</div>");
	}
}
