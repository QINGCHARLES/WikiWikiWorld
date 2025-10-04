using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using System.Diagnostics;

namespace WikiWikiWorld.Web.MarkdigExtensions;

public sealed class CategoriesExtension(List<Data.Models.Category> Categories) : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<CategoriesParser>())
		{
			Pipeline.BlockParsers.Add(new CategoriesParser());
		}
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRendererInstance &&
			!HtmlRendererInstance.ObjectRenderers.Contains<CategoriesRenderer>())
		{
			HtmlRendererInstance.ObjectRenderers.Add(new CategoriesRenderer(Categories));
		}
	}
}

public sealed class CategoriesParser : BlockParser
{
	private const string Marker = "{{Categories}}";

	public CategoriesParser()
	{
		OpeningCharacters = ['{'];
	}

	public override BlockState TryOpen(BlockProcessor Processor)
	{
		int StartPosition = Processor.Line.Start;

		// 🔹 Check if the line starts with "{{Categories}}"
		if (!Processor.Line.Match(Marker))
		{
			return BlockState.None;
		}

		// 🔹 Move cursor forward to avoid infinite loop
		Processor.Line.Start += Marker.Length;

		// 🔹 Push new block
		Processor.NewBlocks.Push(new CategoriesBlock(this));
		return BlockState.BreakDiscard;
	}
}


public sealed class CategoriesBlock : LeafBlock
{
	public CategoriesBlock(BlockParser Parser) : base(Parser) {

		ProcessInlines = false;
	}
}

public sealed class CategoriesRenderer(List<Data.Models.Category> Categories) : HtmlObjectRenderer<CategoriesBlock>
{
	protected override void Write(HtmlRenderer Renderer, CategoriesBlock Block)
	{
		if (Categories is not null && Categories.Count > 0)
		{
			Renderer.WriteLine("<ul class=\"categories\">"); // Use WriteLine here
			WriteCategories(Renderer, Data.Models.Category.PriorityOptions.Primary);
			WriteCategories(Renderer, Data.Models.Category.PriorityOptions.Secondary);
			Renderer.WriteLine("</ul>"); // Use WriteLine here
		}
	}

	private void WriteCategories(HtmlRenderer Renderer, Data.Models.Category.PriorityOptions Priority)
	{
		foreach (Data.Models.Category CategoryItem in Categories)
		{
			if (CategoryItem.Priority == Priority)
			{
				string Url = string.IsNullOrWhiteSpace(CategoryItem.UrlSlug)
					? Slugify(CategoryItem.Title)
					: CategoryItem.UrlSlug;

				Renderer.Write("<li>"); // Write the opening tag
				Renderer.Write("<a class=\"button\" href=\"/category:");
				Renderer.WriteEscapeUrl(Url); // Crucial: Properly escape the URL
				Renderer.Write("\">");
				Renderer.WriteEscape(CategoryItem.Title); // Crucial: Escape the title
				Renderer.Write("</a>");
				Renderer.WriteLine("</li>");  // Write the closing tag on its own line with WriteLine.
			}
		}
	}

	private static string Slugify(string Input)
	{
		// Basic slugify: trim, replace spaces with hyphens, and convert to lower-case.
		return Input.Trim().Replace(" ", "-").ToLowerInvariant();
	}
}