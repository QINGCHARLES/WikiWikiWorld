using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;
using WikiWikiWorld.Web.MarkdigExtensions;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies category disclosure boundaries without dropping, reordering or exposing unsafe labels.
/// </summary>
public sealed class CategoryDisclosureTests
{
	/// <summary>
	/// Keeps the initial list bounded and creates a closed disclosure only when needed.
	/// </summary>
	/// <param name="Count">The number of category markers in the article.</param>
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(8)]
	[InlineData(9)]
	[InlineData(18)]
	public void ShowsInitialCategoriesAndPreservesOverflow(int Count)
	{
		string Source = string.Join("\n", Enumerable.Range(1, Count).Select(i => $"{{{{Category Category {i}}}}}")) + "\n\n{{Categories}}";
		MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Use(new CategoryExtension()).Build();
		IDocument Document = new HtmlParser().ParseDocument(Markdown.ToHtml(Source, Pipeline));

		Assert.Equal(Count, Document.QuerySelectorAll(".categories a").Length);
		Assert.Equal(Math.Min(Count, CategoryExtension.InitialVisibleCategoryCount), Document.QuerySelectorAll(".article-categories > .categories a").Length);
		IElement? Disclosure = Document.QuerySelector(".categories-disclosure");
		if (Count <= CategoryExtension.InitialVisibleCategoryCount)
		{
			Assert.Null(Disclosure);
		}
		else
		{
			Assert.NotNull(Disclosure);
			Assert.False(Disclosure.HasAttribute("open"));
			Assert.Equal($"… Show {Count - CategoryExtension.InitialVisibleCategoryCount} more", Disclosure.QuerySelector(".categories-show-more")!.TextContent);
			Assert.Equal(Count - CategoryExtension.InitialVisibleCategoryCount, Disclosure.QuerySelectorAll("a").Length);
		}
		Assert.Equal(Enumerable.Range(1, Count).Select(i => $"/category:category-{i}"),
			Document.QuerySelectorAll(".categories a").Select(a => a.GetAttribute("href")));
	}

	/// <summary>
	/// Keeps category text escaped in the expanded list as well as the initial list.
	/// </summary>
	[Fact]
	public void OverflowLabelsRemainEscaped()
	{
		string Source = string.Concat(Enumerable.Repeat("{{Category Ordinary}}\n", CategoryExtension.InitialVisibleCategoryCount))
			+ "{{Category <img src=x onerror=alert(1)>}}\n\n{{Categories}}";
		MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Use(new CategoryExtension()).Build();
		IDocument Document = new HtmlParser().ParseDocument(Markdown.ToHtml(Source, Pipeline));
		Assert.Empty(Document.QuerySelectorAll("img"));
		Assert.Equal("<img src=x onerror=alert(1)>", Document.QuerySelector("details a")!.TextContent);
	}
}
