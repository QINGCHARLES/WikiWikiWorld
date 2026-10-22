using AngleSharp.Html.Parser;
using Markdig;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Web.MarkdigExtensions;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies Unicode-preserving category identities and the explicit label/slug authoring contract.
/// </summary>
public sealed class CategorySlugTests
{
	/// <summary>
	/// Exercises Django-style conversion plus intentional preservation of combining marks and supplementary letters.
	/// </summary>
	/// <param name="Label">The native-script or punctuation-bearing source label.</param>
	/// <param name="Expected">The stable expected category identity.</param>
	[Theory]
	[InlineData(" Joel is a slug ", "joel-is-a-slug")]
	[InlineData("你好 World", "你好-world")]
	[InlineData(" __Fashion \t -- Magazines__ ", "fashion-magazines")]
	[InlineData("Mode française", "mode-française")]
	[InlineData("Cafe\u0301", "café")]
	[InlineData("Ｆａｓｈｉｏｎ　Magazines", "fashion-magazines")]
	[InlineData("日本の雑誌", "日本の雑誌")]
	[InlineData("مجلات الموضة", "مجلات-الموضة")]
	[InlineData("हिन्दी पत्रिकाएँ", "हिन्दी-पत्रिकाएँ")]
	[InlineData("นิตยสาร", "นิตยสาร")]
	[InlineData("𠮷野 雑誌", "𠮷野-雑誌")]
	[InlineData("GQ Style (USA) magazines", "gq-style-usa-magazines")]
	[InlineData("Women's magazines", "womens-magazines")]
	[InlineData(" /?#% ", "")]
	public void GeneratesNativeScriptSlugs(string Label, string Expected)
	{
		Assert.Equal(Expected, CategorySlugHelper.Generate(Label));
		Assert.Equal(Expected, CategorySlugHelper.Generate(Expected));
	}

	/// <summary>
	/// Ensures the explicit identity survives independently of the display label and is used by rendering.
	/// </summary>
	[Fact]
	public void ExplicitSlugControlsMetadataAndLinks()
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Use(new CategoryExtension()).Build();
		Markdig.Syntax.MarkdownDocument Document = Markdown.Parse(
			"{{Category Fashion magazines |#| Fashion-(Original)}}\n{{Category Mode française|#|Fashion-(Original)}}\n\n{{Categories}}", Pipeline);
		Assert.All(CategoryExtension.GetCategories(Document), c => Assert.Equal("Fashion-(Original)", c.UrlSlug));
		Assert.Equal("Fashion magazines", CategoryExtension.GetCategories(Document)[0].Title);
		string Html = Markdown.ToHtml(Document, Pipeline);
		AngleSharp.Dom.IDocument Rendered = new HtmlParser().ParseDocument(Html);
		Assert.All(Rendered.QuerySelectorAll(".categories a"), a => Assert.Equal("/category:Fashion-(Original)", a.GetAttribute("href")));
	}

	/// <summary>
	/// Leaves malformed category references visible and excludes them from extracted memberships.
	/// </summary>
	/// <param name="Source">A category reference with an unusable identity or missing label.</param>
	[Theory]
	[InlineData("{{Category Label|#|}}")]
	[InlineData("{{Category |#|slug}}")]
	[InlineData("{{Category Label|#|../other}}")]
	[InlineData("{{Category Label|#|slug?query}}")]
	[InlineData("{{Category Label|#|slug|#|extra}}")]
	[InlineData("{{Category !!!}}")]
	public void InvalidReferencesStayVisible(string Source)
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Use(new CategoryExtension()).Build();
		Markdig.Syntax.MarkdownDocument Document = Markdown.Parse(Source, Pipeline);
		Assert.Empty(CategoryExtension.GetCategories(Document));
		Assert.Contains(Source, new HtmlParser().ParseDocument(Markdown.ToHtml(Document, Pipeline)).Body!.TextContent);
	}

	/// <summary>
	/// Excludes fenced and inline code examples while resolving a real shorthand category.
	/// </summary>
	[Fact]
	public void CodeExamplesDoNotDeclareMembership()
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Use(new CategoryExtension()).Build();
		Markdig.Syntax.MarkdownDocument Document = Markdown.Parse(
			"`{{Category Example}}`\n\n```\n{{Category Example}}\n```\n\n{{Category Fashion Magazines}}", Pipeline);
		Assert.Equal("fashion-magazines", Assert.Single(CategoryExtension.GetCategories(Document)).UrlSlug);
	}
}
