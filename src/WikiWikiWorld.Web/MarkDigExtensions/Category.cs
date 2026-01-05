using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A Markdown inline element representing a category tag.
/// </summary>
public sealed class CategoryInline : LeafInline
{
	/// <summary>
	/// Gets or initializes the raw string data of the category.
	/// </summary>
	public required StringSlice Data { get; init; }
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

		int InlineStart = Processor.GetSourcePosition(Slice.Start, out int Line, out int Column);

		Processor.Inline = new CategoryInline
		{
			Span = new SourceSpan(InlineStart, InlineStart + (EndPos - Slice.Start)),
			Line = Line,
			Column = Column,
			Data = new StringSlice(Slice.Text, Slice.Start, EndPos - 1)
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
	/// Extracts a list of categories from a parsed Markdown document.
	/// </summary>
	/// <param name="Document">The markdown document to inspect.</param>
	/// <returns>A list of distinct categories found in the document.</returns>
	public static List<Category> GetCategories(MarkdownDocument Document)
	{
		return [.. Document.Descendants<CategoryInline>()
			.Select(c => new Category { Title = c.Data.ToString().Trim() })
			.Where(c => !string.IsNullOrEmpty(c.Title))];
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
/// Renders a <see cref="CategoriesBlock"/> as an HTML unordered list.
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

		var Categories = CategoryExtension.GetCategories(Document);
		if (Categories.Count == 0) return;

		Renderer.WriteLine("<ul class=\"categories\">");
		foreach (var CategoryItem in Categories)
		{
			string Url = Slugify(CategoryItem.Title);
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

	private static string Slugify(string Input)
	{
		return Input.Trim().Replace(" ", "-").ToLowerInvariant();
	}
}
