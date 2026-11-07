using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using WikiWikiWorld.Web.MarkdigExtensions;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies that publication facts and cover images have independent rendering and extraction paths.
/// </summary>
public sealed class PublicationIssueCoverTests
{
	/// <summary>
	/// Ensures the infobox renders publication facts only, with no cover carousel or image chrome.
	/// </summary>
	[Fact]
	public void InfoboxRendersFactsOnly()
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
			.Use(new PublicationIssueInfoboxExtension()).Build();
		string Html = Markdown.ToHtml(
			"{{PublicationIssueInfobox CoverDate=1993-01 |#| Editor=Alice |#| Editor=Bob}}",
			Pipeline);

		Assert.Contains("<dt>Cover Date</dt><dd>1993-01</dd>", Html);
		Assert.Contains("<dt>Editor</dt><dd>Alice</dd><dt>Editor</dt><dd>Bob</dd>", Html);
		Assert.DoesNotContain("<img", Html);
		Assert.DoesNotContain("carousel", Html);
	}

	/// <summary>
	/// Ensures the standalone carousel owns single-image and multi-image rendering and escapes HTML attributes.
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
			Block.CachedCoverImages.Add(new($"/cover-{i}.jpg", $"Cover \"{i}\" & <more>", null));
		}
		using StringWriter Writer = new();
		HtmlRenderer Renderer = new(Writer);
		Pipeline.Setup(Renderer);
		Renderer.Render(Document);
		string Html = Writer.ToString();

		Assert.Contains("cover-carousel", Html);
		Assert.Contains("/cover-0.jpg", Html);
		Assert.Contains("alt=\"Cover &quot;0&quot; &amp; &lt;more&gt;\"", Html);
		Assert.Equal(Count > 1, Html.Contains("class=\"dots\"", StringComparison.Ordinal));
		Assert.DoesNotContain("<dl>", Html);
	}

	/// <summary>
	/// Protects listing cover selection from <c>{{CoverCarousel}}</c> directives and ignores infoboxes.
	/// </summary>
	/// <param name="Source">Article source containing directives.</param>
	/// <param name="Expected">The primary file slug, if present.</param>
	[Theory]
	[InlineData("{{CoverCarousel CoverImage=file:front |~| CoverImage=back}}", "front")]
	[InlineData("{{CoverCarousel AltText=Front |#| coverimage=file:front |#| Caption=Caption}}", "front")]
	[InlineData("{{CoverCarousel CoverImage=old |#| CoverImage=front}}", "front")]
	[InlineData("{{CoverCarousel AltText=Empty |~| CoverImage=front}}", "front")]
	[InlineData("{{PublicationIssueInfobox CoverDate=2024}}{{CoverCarousel CoverImage=front}}", "front")]
	[InlineData("{{PublicationIssueInfobox CoverImage=file:legacy |#| CoverDate=2024}}", null)]
	[InlineData("{{PublicationIssueInfobox CoverDate=2024}}", null)]
	[InlineData("{{CoverCarouselExtra CoverImage=front}}", null)]
	[InlineData("{{CoverCarousel CoverImage=unfinished", null)]
	public void ListingsExtractPrimaryCoverFromCoverCarouselOnly(string Source, string? Expected)
	{
		Assert.Equal(Expected, ArticleCoverReference.Extract(Source));
	}
}
