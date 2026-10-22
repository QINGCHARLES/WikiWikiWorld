using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Helpers;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A Markdown inline element representing a category tag.
/// </summary>
public sealed class CategoryInline : LeafInline
{
	/// <summary>
	/// Gets the display label and resolved category identity parsed from the directive.
	/// </summary>
	public required Category Category { get; init; }
}

/// <summary>
/// Parses {{Category ...}} syntaxes in Markdown.
/// </summary>
public sealed class CategoryParser : InlineParser
{
	private const string MarkerStart = "{{Category ";
	private const string MarkerEnd = "}}";

	/// <summary>
	/// Initializes a new instance of the <see cref="CategoryParser"/> class.
	/// </summary>
	public CategoryParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override bool Match(InlineProcessor Processor, ref StringSlice Slice)
	{
		int StartPosition = Slice.Start;
		if (!Slice.Match(MarkerStart)) return false;

		Slice.Start += MarkerStart.Length;
		int EndPos = Slice.Text.IndexOf(MarkerEnd, Slice.Start, StringComparison.Ordinal);
		
		if (EndPos == -1)
		{
			Slice.Start = StartPosition;
			return false;
		}

		string Payload = Slice.Text[Slice.Start..EndPos];
		int Separator = Payload.IndexOf("|#|", StringComparison.Ordinal);
		string Label = (Separator < 0 ? Payload : Payload[..Separator]).Trim();
		string Slug;
		try
		{
			Slug = Separator < 0 ? CategorySlugHelper.Generate(Label) : Payload[(Separator + 3)..].Trim();
		}
		catch (ArgumentException)
		{
			Slice.Start = StartPosition;
			return false;
		}
		if (Label.Length == 0 || !CategorySlugHelper.IsValidExplicitSlug(Slug))
		{
			Slice.Start = StartPosition;
			return false;
		}

		int InlineStart = Processor.GetSourcePosition(Slice.Start, out int Line, out int Column);

		Processor.Inline = new CategoryInline
		{
			Span = new SourceSpan(InlineStart, InlineStart + (EndPos - Slice.Start)),
			Line = Line,
			Column = Column,
			Category = new Category { Title = Label, UrlSlug = Slug }
		};

		Slice.Start = EndPos + MarkerEnd.Length;
		return true;
	}
}

/// <summary>
/// Renders a <see cref="CategoryInline"/> into HTML (No-Op as categories are typically not rendered inline).
/// </summary>
public sealed class CategoryRenderer : HtmlObjectRenderer<CategoryInline>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, CategoryInline Inline) { /* No-Op */ }
}

/// <summary>
/// A Markdig extension that adds support for categories.
/// </summary>
public sealed class CategoryExtension : IMarkdownExtension
{
	/// <summary>
	/// Number of category links shown before the reader expands the remaining categories.
	/// </summary>
	public const int InitialVisibleCategoryCount = 8;

	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.InlineParsers.Contains<CategoryParser>())
			Pipeline.InlineParsers.Add(new CategoryParser());

		if (!Pipeline.BlockParsers.Contains<CategoriesBlockParser>())
			Pipeline.BlockParsers.Add(new CategoriesBlockParser());
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer)
		{
			if (!HtmlRenderer.ObjectRenderers.Contains<CategoryRenderer>())
				HtmlRenderer.ObjectRenderers.Add(new CategoryRenderer());

			if (!HtmlRenderer.ObjectRenderers.Contains<CategoriesRenderer>())
				HtmlRenderer.ObjectRenderers.Add(new CategoriesRenderer());
		}
	}

	/// <summary>
	/// Extracts resolved category labels and slugs in source order, retaining repeated references.
	/// </summary>
	/// <param name="Document">The markdown document to inspect.</param>
	/// <returns>The valid category references found in the document, including duplicates.</returns>
	public static List<Category> GetCategories(MarkdownDocument Document)
	{
		return [.. Document.Descendants<CategoryInline>()
			.Select(c => c.Category)];
	}
}

/// <summary>
/// A block element representing the list of categories in a document.
/// </summary>
/// <param name="Parser">The block parser used to create this block.</param>
public sealed class CategoriesBlock(BlockParser Parser) : LeafBlock(Parser)
{
}

/// <summary>
/// Parses the {{Categories}} block marker.
/// </summary>
public sealed class CategoriesBlockParser : BlockParser
{
	private const string Marker = "{{Categories}}";

	/// <summary>
	/// Initializes a new instance of the <see cref="CategoriesBlockParser"/> class.
	/// </summary>
	public CategoriesBlockParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		if (!Processor.Line.Match(Marker)) return BlockState.None;

		Processor.Line.Start += Marker.Length;
		Processor.NewBlocks.Push(new CategoriesBlock(this));
		return BlockState.BreakDiscard;
	}
}

/// <summary>
/// Renders category links with a native disclosure for entries beyond the initial visible limit.
/// </summary>
public sealed class CategoriesRenderer : HtmlObjectRenderer<CategoriesBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, CategoriesBlock Block)
	{
		// Traverse up to find the root document
		Block Root = Block;
		while (Root.Parent is not null)
		{
			Root = Root.Parent;
		}

		if (Root is not MarkdownDocument Document) return;

		List<Category> Categories = CategoryExtension.GetCategories(Document);
		if (Categories.Count == 0) return;

		Renderer.WriteLine("<div class=\"article-categories\">");
		Renderer.WriteLine("<h2 class=\"categories-label\">Categories</h2>");
		RenderList(Renderer, Categories.Take(CategoryExtension.InitialVisibleCategoryCount));

		int RemainingCount = Categories.Count - CategoryExtension.InitialVisibleCategoryCount;
		if (RemainingCount > 0)
		{
			Renderer.WriteLine("<details class=\"categories-disclosure\">");
			Renderer.Write("<summary class=\"categories-toggle\"><span class=\"categories-show-more\">");
			Renderer.Write($"… Show {RemainingCount} more");
			Renderer.WriteLine("</span><span class=\"categories-show-fewer\">Show fewer</span></summary>");
			RenderList(Renderer, Categories.Skip(CategoryExtension.InitialVisibleCategoryCount));
			Renderer.WriteLine("</details>");
		}
		Renderer.WriteLine("</div>");
	}

	/// <summary>
	/// Writes category links in source order, escaping their labels and URLs.
	/// </summary>
	/// <param name="Renderer">The HTML output writer.</param>
	/// <param name="Categories">The visible or expandable subset of categories.</param>
	private static void RenderList(HtmlRenderer Renderer, IEnumerable<Category> Categories)
	{
		Renderer.WriteLine("<ul class=\"categories\" role=\"list\">");
		foreach (Category CategoryItem in Categories)
		{
			string Url = CategoryItem.UrlSlug!;
			Renderer.Write("<li>");
			Renderer.Write("<a class=\"button\" href=\"/category:");
			Renderer.WriteEscapeUrl(Url);
			Renderer.Write("\">");
			Renderer.WriteEscape(CategoryItem.Title);
			Renderer.Write("</a>");
			Renderer.WriteLine("</li>");
		}
		Renderer.WriteLine("</ul>");
	}

}
