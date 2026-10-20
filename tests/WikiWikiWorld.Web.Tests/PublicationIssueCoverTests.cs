using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using WikiWikiWorld.Web.MarkdigExtensions;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies that publication facts and cover images have independent rendering paths.
/// </summary>
public sealed class PublicationIssueCoverTests
{
	/// <summary>
	/// Ensures old cover properties never leak images or file slugs into the facts list.
	/// </summary>
	[Fact]
	public void InfoboxRendersFactsWithoutCovers()
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
			.Use(new PublicationIssueInfoboxExtension()).Build();
		string Html = Markdown.ToHtml(
			"{{PublicationIssueInfobox CoverImage=file:front |#| coverimage=back |#| CoverDate=1993-01 |#| Editor=Alice |#| Editor=Bob}}",
			Pipeline);

		Assert.Contains("<dt>Cover Date</dt><dd>1993-01</dd>", Html);
		Assert.Contains("<dt>Editor</dt><dd>Alice</dd><dt>Editor</dt><dd>Bob</dd>", Html);
		Assert.DoesNotContain("front", Html);
		Assert.DoesNotContain("back", Html);
		Assert.DoesNotContain("<img", Html);
		Assert.DoesNotContain("carousel", Html);
	}

	/// <summary>
	/// Ensures the standalone carousel still owns single-image and multi-image rendering.
	/// </summary>
	/// <param name="Count">The number of resolved covers.</param>
	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	public void StandaloneCarouselKeepsCoverRendering(int Count)
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
			.Use(new CoverCarouselExtension()).Build();
		MarkdownDocument Document = Markdown.Parse("{{CoverCarousel CoverImage=front}}", Pipeline);
		CoverCarouselBlock Block = Document.Descendants<CoverCarouselBlock>().Single();
		for (int i = 0; i < Count; i++)
		{
			Block.CachedCoverImages.Add(new($"/cover-{i}.jpg", $"Cover {i}", null));
		}
		using StringWriter Writer = new();
		HtmlRenderer Renderer = new(Writer);
		Pipeline.Setup(Renderer);
		Renderer.Render(Document);
		string Html = Writer.ToString();

		Assert.Contains("cover-carousel", Html);
		Assert.Contains("/cover-0.jpg", Html);
		Assert.Equal(Count > 1, Html.Contains("class=\"dots\"", StringComparison.Ordinal));
		Assert.DoesNotContain("<dl>", Html);
	}

	/// <summary>
	/// Protects listing cover selection across new syntax and unmigrated article metadata.
	/// </summary>
	/// <param name="Source">Article source containing cover directives.</param>
	/// <param name="Expected">The primary file slug, if present.</param>
	[Theory]
	[InlineData("{{CoverCarousel CoverImage=file:front |~| CoverImage=back}}", "front")]
	[InlineData("{{CoverCarousel AltText=Front |#| coverimage=file:front |#| Caption=Caption}}", "front")]
	[InlineData("{{CoverCarousel CoverImage=old |#| CoverImage=front}}", "front")]
	[InlineData("{{CoverCarousel AltText=Empty |~| CoverImage=front}}", "front")]
	[InlineData("{{PublicationIssueInfobox CoverImage=legacy}}{{CoverCarousel CoverImage=front}}", "front")]
	[InlineData("{{PublicationIssueInfobox CoverImage=file:legacy |#| CoverDate=2024}}", "legacy")]
	[InlineData("{{PublicationIssueInfobox CoverDate=2024}}", null)]
	[InlineData("{{CoverCarousel CoverImage=unfinished", null)]
	public void ListingsPreferCarouselWithLegacyFallback(string Source, string? Expected)
	{
		Assert.Equal(Expected, ArticleCoverReference.Extract(Source));
	}
}
