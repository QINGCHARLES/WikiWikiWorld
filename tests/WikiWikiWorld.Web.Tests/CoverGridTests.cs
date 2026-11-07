using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using WikiWikiWorld.Web.MarkdigExtensions;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Protects cover links against adding culture path segments on culture-specific hosts and verifies HTML escaping.
/// </summary>
public sealed class CoverGridTests
{
	/// <summary>
	/// Ensures every cover links directly to its article from a nested page in any culture and escapes titles.
	/// </summary>
	/// <param name="Culture">The culture supplied during cover enrichment.</param>
	[Theory]
	[InlineData("en")]
	[InlineData("fr")]
	[InlineData("ja")]
	public void CoverLinksUseArticlePathsOnCurrentHost(string Culture)
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
			.Use(new CoverGridExtension()).Build();
		MarkdownDocument Document = Markdown.Parse("{{CoverGrid\nfirst-issue\nsecond-issue\n}}", Pipeline);
		CoverGridBlock Block = Document.Descendants<CoverGridBlock>().Single();
		Block.Culture = Culture;
		Block.CachedCoverImages.Add(("/first-cover.jpg", "First \"issue\" & <more>", "first-issue"));
		Block.CachedCoverImages.Add(("/second-cover.jpg", "Second issue", "second-issue"));

		using StringWriter Writer = new();
		HtmlRenderer Renderer = new(Writer);
		Pipeline.Setup(Renderer);
		Renderer.Render(Document);
		string RenderedHtml = Writer.ToString();
		using IDocument Html = new HtmlParser().ParseDocument(RenderedHtml);
		string[] Links = [.. Html.QuerySelectorAll(".cover-grid a").Select(x => x.GetAttribute("href")!)];

		Assert.Equal(["/first-issue", "/second-issue"], Links);
		Assert.DoesNotContain("<style>", RenderedHtml);
		Assert.Equal("First \"issue\" & <more>", Html.QuerySelector(".cover-grid figure img")!.GetAttribute("alt"));
		Assert.Equal("First \"issue\" & <more>", Html.QuerySelector(".cover-grid figcaption")!.TextContent);
		Uri CurrentPage = new($"https://{Culture}.localhost:7126/magazine/issues");
		Assert.Equal($"https://{Culture}.localhost:7126/first-issue", new Uri(CurrentPage, Links[0]).AbsoluteUri);
		Assert.Equal($"https://{Culture}.localhost:7126/second-issue", new Uri(CurrentPage, Links[1]).AbsoluteUri);
	}
}
