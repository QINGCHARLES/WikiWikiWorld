using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;
using WikiWikiWorld.Web.Pages;

namespace WikiWikiWorld.Web.MarkdigExtensions;

public class ImageInline : LeafInline
{
	public required string UrlSlug { get; init; }
	public string? Type { get; init; }
}

public class ImageInlineParser : InlineParser
{
	private const string MarkerStart = "{{Image ";
	private const string MarkerEnd = "}}";
	private const string AttributeSeparator = "|#|";

	public ImageInlineParser()
	{
		OpeningCharacters = ['{'];
	}

	public override bool Match(InlineProcessor Processor, ref StringSlice Slice)
	{
		int StartPosition = Slice.Start;
		if (!Slice.Match(MarkerStart))
		{
			return false;
		}

		Slice.Start += MarkerStart.Length;
		int EndPos = Slice.Text.IndexOf(MarkerEnd, Slice.Start, StringComparison.Ordinal);
		if (EndPos == -1)
		{
			Slice.Start = StartPosition;
			return false;
		}

		// Extract main data before "|#|"
		int AttributeStart = Slice.Text.IndexOf(AttributeSeparator, Slice.Start, EndPos - Slice.Start, StringComparison.Ordinal);
		string UrlSlug;
		string? Type = null;

		if (AttributeStart != -1)
		{
			UrlSlug = Slice.Text.Substring(Slice.Start, AttributeStart - Slice.Start).Trim();
			string AttributesPart = Slice.Text.Substring(AttributeStart + AttributeSeparator.Length, EndPos - AttributeStart - AttributeSeparator.Length).Trim();

			// Extract attributes
			string[] Attributes = AttributesPart.Split('|', StringSplitOptions.RemoveEmptyEntries);
			foreach (string Attribute in Attributes)
			{
				string[] Parts = Attribute.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
				if (Parts.Length == 2 && Parts[0].Trim().Equals("Type", StringComparison.OrdinalIgnoreCase))
				{
					Type = Parts[1].Trim();
				}
			}
		}
		else
		{
			UrlSlug = Slice.Text.Substring(Slice.Start, EndPos - Slice.Start).Trim();
		}

		// Create the inline element
		ImageInline InlineElement = new()
		{
			UrlSlug = UrlSlug,
			Type = Type
		};

		Processor.Inline = InlineElement;
		Slice.Start = EndPos + MarkerEnd.Length;
		return true;
	}
}

public class ImageInlineRenderer(int SiteId, string Culture, IArticleRevisionRepository ArticleRepository, IFileRevisionRepository FileRepository, BasePageModel PageModel) : HtmlObjectRenderer<ImageInline>
{
	protected override void Write(HtmlRenderer Renderer, ImageInline InlineElement)
	{
		// Fetch the article revision (blocking call)
		ArticleRevision? Article = ArticleRepository
			.GetCurrentBySiteIdCultureAndUrlSlugAsync(SiteId, Culture, InlineElement.UrlSlug.Replace("file:", ""))
			.GetAwaiter()
			.GetResult();

		if (Article is null)
		{
			Renderer.Write($"<!-- Article not found for UrlSlug: {InlineElement.UrlSlug}  -->");
			Renderer.Write($"<img src=\"/sitefiles/{SiteId}/missing-image.png\" alt=\"Missing Image\" />");
			return;
		}

		// Fetch the file revision (blocking call)
		FileRevision? File = FileRepository
			.GetCurrentByCanonicalFileIdAsync(Article.CanonicalFileId!.Value)
			.GetAwaiter()
			.GetResult();

		if (File is null)
		{
			Renderer.Write($"<!-- File not found for CanonicalFileId: {Article.CanonicalFileId} -->");
			Renderer.Write($"<img src=\"/sitefiles/{SiteId}/missing-image.png\" alt=\"Missing Image\" />");
			return;
		}

		string ImageUrl = $"/sitefiles/{SiteId}/images/{File.CanonicalFileId}{Path.GetExtension(File.Filename)}";

		// Handle "Header" type as a CSS background
		if (InlineElement.Type?.Equals("Header", StringComparison.OrdinalIgnoreCase) == true)
		{
			// Set the HeaderImage property on the PageModel
			PageModel.HeaderImage = ImageUrl;
		}
		else
		{
			// Determine the class based on the type
			string? Class = InlineElement.Type?.ToLower() switch
			{
				"full" => "full",
				"breakout" => "breakout",
				"max" => "max",
				_ => null
			};

			// Regular <img> tag with optional class
			Renderer.Write($"<img src=\"{ImageUrl}\" alt=\"{Article.Title}\"");
			if (Class is not null)
			{
				Renderer.Write($" class=\"{Class}\"");
			}
			Renderer.Write(" />");
		}
	}
}

public class ImageExtension(int SiteId, string Culture, IArticleRevisionRepository ArticleRepository, IFileRevisionRepository FileRepository, BasePageModel PageModel) : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.InlineParsers.Contains<ImageInlineParser>())
		{
			Pipeline.InlineParsers.Add(new ImageInlineParser());
		}
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer &&
			!HtmlRenderer.ObjectRenderers.Any(r => r is ImageInlineRenderer))
		{
			HtmlRenderer.ObjectRenderers.Add(new ImageInlineRenderer(SiteId, Culture, ArticleRepository, FileRepository, PageModel));
		}
	}
}