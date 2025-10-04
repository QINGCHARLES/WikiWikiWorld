using System.Text;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace WikiWikiWorld.Web.MarkdigExtensions;

public class MagazineInfoboxBlock(BlockParser Parser) : LeafBlock(Parser)
{
	public StringBuilder RawContent { get; } = new();
	public Dictionary<string, List<string>> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public class MagazineInfoboxBlockParser : BlockParser
{
	private const string MarkerStart = "{{MagazineInfobox";
	private const string MarkerEnd = "}}";

	public MagazineInfoboxBlockParser() => OpeningCharacters = ['{'];

	public override BlockState TryOpen(BlockProcessor Processor)
	{
		StringSlice Slice = Processor.Line;
		if (!Slice.Match(MarkerStart))
		{
			return BlockState.None;
		}

		MagazineInfoboxBlock MagazineBlock = new(this)
		{
			Line = Processor.LineIndex,
			Column = Processor.Column
		};

		Slice.Start += MarkerStart.Length;
		string Remaining = Slice.ToString();
		if (!string.IsNullOrWhiteSpace(Remaining))
		{
			int EndPos = Remaining.IndexOf(MarkerEnd, StringComparison.Ordinal);
			if (EndPos != -1)
			{
				MagazineBlock.RawContent.AppendLine(Remaining[..EndPos]);
				Processor.NewBlocks.Push(MagazineBlock);
				return BlockState.Break;
			}
			MagazineBlock.RawContent.AppendLine(Remaining);
		}

		Processor.NewBlocks.Push(MagazineBlock);
		return BlockState.Continue;
	}

	public override BlockState TryContinue(BlockProcessor Processor, Block CurrentBlock)
	{
		StringSlice Slice = Processor.Line;
		string Line = Slice.ToString();
		int EndPos = Line.IndexOf(MarkerEnd, StringComparison.Ordinal);
		MagazineInfoboxBlock MagazineBlock = CurrentBlock as MagazineInfoboxBlock ?? throw new InvalidOperationException("Block is not a MagazineInfoboxBlock.");

		if (EndPos != -1)
		{
			MagazineBlock.RawContent.AppendLine(Line[..EndPos]);
			return BlockState.Break;
		}
		MagazineBlock.RawContent.AppendLine(Line);
		return BlockState.Continue;
	}
}

public class MagazineInfoboxRenderer(int SiteId, string Culture, IArticleRevisionRepository ArticleRepository, IFileRevisionRepository FileRepository) : HtmlObjectRenderer<MagazineInfoboxBlock>
{
	// Default number of slides in the standard CSS
	private const int MaxSlides = 3;

	// Maximum number of slides we'll support with inline CSS
	private const int MaxTotalSlides = 10;

	protected override void Write(HtmlRenderer Renderer, MagazineInfoboxBlock Block)
	{
		ArgumentNullException.ThrowIfNull(Renderer);
		ArgumentNullException.ThrowIfNull(Block);

		const string AttributeSeparator = "|#|";
		string Raw = Block.RawContent.ToString();
		string[] Tokens = Raw.Split(AttributeSeparator, StringSplitOptions.RemoveEmptyEntries);

		foreach (string Token in Tokens)
		{
			string Trimmed = Token.Trim();
			if (string.IsNullOrEmpty(Trimmed))
			{
				continue;
			}

			int EqPos = Trimmed.IndexOf('=');
			if (EqPos <= 0)
			{
				continue;
		}

		string Key = Trimmed[..EqPos].Trim();
		string Value = Trimmed[(EqPos + 1)..].Trim();

		if (!Block.Properties.TryGetValue(Key, out List<string>? List))
		{
			List = [];
			Block.Properties[Key] = List;
		}
		List.Add(Value);
	}		Renderer.Write("<aside class=\"infobox\">");

		if (Block.Properties.TryGetValue("CoverImage", out List<string>? CoverImages) && CoverImages.Count > 0)
		{
			// Collect valid images
			List<(string ImageUrl, string AltText)> Images = [];

			foreach (string Cover in CoverImages)
			{
				string UrlSlug = Cover.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
					? Cover["file:".Length..]
					: Cover;

				// Fetch the article revision (blocking call)
				ArticleRevision? Article = ArticleRepository
					.GetCurrentBySiteIdCultureAndUrlSlugAsync(SiteId, Culture, UrlSlug)
					.GetAwaiter().GetResult();

				if (Article is null)
				{
					// Add a missing image placeholder
					Images.Add(($"/sitefiles/{SiteId}/missing-image.png", "Missing Image"));
					continue;
				}

				// Fetch the file revision (blocking call)
				FileRevision? File = FileRepository
					.GetCurrentByCanonicalFileIdAsync(Article.CanonicalFileId!.Value)
					.GetAwaiter().GetResult();

				if (File is null)
				{
					// Add a missing image placeholder
					Images.Add(($"/sitefiles/{SiteId}/missing-image.png", "Missing Image"));
					continue;
				}

				string ImageUrl = $"/sitefiles/{SiteId}/images/{File.CanonicalFileId}{Path.GetExtension(File.Filename)}";
				Images.Add((ImageUrl, Article.Title ?? "Magazine Cover"));
			}

			// If there's only one image, render it directly
			if (Images.Count == 1)
			{
				(string ImageUrl, string AltText) = Images[0];
				Renderer.Write($"<img src=\"{ImageUrl}\" alt=\"{AltText}\" />");
			}
			// If there are multiple images, render the carousel
			else if (Images.Count > 1)
			{
			// Limit to maximum supported slides (including inline CSS extensions)
			int SlidesToRender = Math.Min(Images.Count, MaxTotalSlides);
			RenderCarousel(Renderer, [.. Images.Take(SlidesToRender)]);
		}
	}

	List<KeyValuePair<string, List<string>>> OtherProps = [.. Block.Properties
		.Where(KeyValuePair => !KeyValuePair.Key.Equals("CoverImage", StringComparison.OrdinalIgnoreCase))];

		if (OtherProps.Any())
		{
			Renderer.Write("<ul>");
			foreach (KeyValuePair<string, List<string>> KeyValuePair in OtherProps)
			{
				string Friendly = GetFriendlyName(KeyValuePair.Key);
				foreach (string Val in KeyValuePair.Value)
				{
					Renderer.Write($"<li><strong>{Friendly}:</strong> {Val}</li>");
				}
			}
			Renderer.Write("</ul>");
		}

		Renderer.Write("</aside>");
	}

	// New method to render the carousel
	private void RenderCarousel(HtmlRenderer Renderer, List<(string ImageUrl, string AltText)> Images)
	{
		int ImageCount = Images.Count;

		// If we have more than MaxSlides, we need to add inline CSS
		if (ImageCount > MaxSlides)
		{
			Renderer.Write(GenerateInlineCssForExtraSlides(ImageCount));
		}

		Renderer.Write("<div class=\"carousel\">");

		// Hidden radio inputs to control slides
		for (int i = 0; i < ImageCount; i++)
		{
			string Checked = i == 0 ? " checked" : "";
			Renderer.Write($"<input type=\"radio\" name=\"carousel\" id=\"slide{i + 1}\"{Checked}>");
		}

		// Slides container
		Renderer.Write("<div class=\"slides\">");

		// Render each slide
		for (int i = 0; i < ImageCount; i++)
		{
			(string ImageUrl, string AltText) = Images[i];
			Renderer.Write($"<div class=\"slide\">");
			Renderer.Write($"<img src=\"{ImageUrl}\" alt=\"{AltText}\">");
			Renderer.Write("</div>");
		}

		Renderer.Write("</div>");

		// Navigation with context-specific controls
		Renderer.Write("<div class=\"nav\">");

		// Generate nav controls for each slide
		for (int i = 0; i < ImageCount; i++)
		{
			int PrevSlide = (i - 1 + ImageCount) % ImageCount + 1;
			int NextSlide = (i + 1) % ImageCount + 1;

			Renderer.Write($"<div class=\"nav-controls nav-slide{i + 1}\">");

			// Previous button
			Renderer.Write($"<label for=\"slide{PrevSlide}\" class=\"prev\">");
			Renderer.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" class=\"icon icon-tabler icons-tabler-outline icon-tabler-circle-chevron-left\"><path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\" /><path d=\"M13 15l-3 -3l3 -3\" /><path d=\"M21 12a9 9 0 1 0 -18 0a9 9 0 0 0 18 0z\" /></svg>");
			Renderer.Write("</label>");

			// Next button
			Renderer.Write($"<label for=\"slide{NextSlide}\" class=\"next\">");
			Renderer.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" class=\"icon icon-tabler icons-tabler-outline icon-tabler-circle-chevron-right\"><path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\" /><path d=\"M11 9l3 3l-3 3\" /><path d=\"M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0z\" /></svg>");
			Renderer.Write("</label>");

			Renderer.Write("</div>");
		}

		Renderer.Write("</div>");

		// Dot indicators below the carousel
		Renderer.Write("<div class=\"dots\">");
		for (int i = 0; i < ImageCount; i++)
		{
			Renderer.Write($"<label for=\"slide{i + 1}\"></label>");
		}
		Renderer.Write("</div>");

		Renderer.Write("</div>");
	}

	// Method to generate inline CSS for slides beyond the default maximum (3)
	private string GenerateInlineCssForExtraSlides(int ImageCount)
	{
		// Only generate CSS for slides beyond 3
		if (ImageCount <= MaxSlides)
		{
			return string.Empty;
		}

		StringBuilder CssBuilder = new();
		CssBuilder.AppendLine("<style>");

		// Show active slide rules
		for (int i = MaxSlides + 1; i <= ImageCount; i++)
		{
			CssBuilder.AppendLine($"#slide{i}:checked ~ .slides .slide:nth-of-type({i}) {{ display: block; }}");
		}

		// Display controls for active slide
		for (int i = MaxSlides + 1; i <= ImageCount; i++)
		{
			CssBuilder.AppendLine($"#slide{i}:checked ~ .nav .nav-slide{i} {{ display: block; }}");
		}

		// Active dot styling
		for (int i = MaxSlides + 1; i <= ImageCount; i++)
		{
			CssBuilder.AppendLine($"#slide{i}:checked ~ .dots label[for=\"slide{i}\"] {{ background-color: var(--uchu-green-5); }}");
		}

		CssBuilder.AppendLine("</style>");
		return CssBuilder.ToString();
	}

	private static string GetFriendlyName(string Key) => Key switch
	{
		"CoverPrice" => "Cover Price",
		"PublicationFormats" => "Publication Formats",
		"CoverDate" => "Cover Date",
		"PrintPublicationDate" => "Print Publication Date",
		"ElectronicPublicationDate" => "Electronic Publication Date",
		"PrintEAN-13" => "Print EAN-13",
		"PrintEAN-2" => "Print EAN-2",
		"ISSN" => "ISSN",
		_ => Key,
	};
}

public class MagazineInfoboxExtension(int SiteId, string Culture, IArticleRevisionRepository ArticleRepository, IFileRevisionRepository FileRepository) : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<MagazineInfoboxBlockParser>())
		{
			Pipeline.BlockParsers.Insert(0, new MagazineInfoboxBlockParser());
		}
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer &&
			!HtmlRenderer.ObjectRenderers.Any(R => R is MagazineInfoboxRenderer))
		{
			HtmlRenderer.ObjectRenderers.Add(new MagazineInfoboxRenderer(SiteId, Culture, ArticleRepository, FileRepository));
		}
	}
}