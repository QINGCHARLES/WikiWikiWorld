using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies credits authoring, disclosure semantics, safe names, and article-only registration.
/// </summary>
public sealed class IssueCreditsTests
{
	private static readonly MarkdownPipelineFactory Factory = new(Options.Create(new SiteConfiguration()));

	/// <summary>
	/// Checks featured visibility, independent disclosure defaults, names, and source ordering together.
	/// </summary>
	[Fact]
	public void GroupsPreserveContentAndDisclosureChoices()
	{
		const string Source = """
			{{IssueCredits
			Group=Editor-in-Chief |#| Featured=true
			|~| Name=[Anna Wintour](/anna-wintour)
			|~| Group=Editorial Leadership
			|~| Role=Creative Director |#| Name=Grace Coddington
			|~| Role=Art Director |#| Name=Roxanne Behr
			|~| Group=Photography |#| Open=true
			|~| Role=Editorial |#| Name=**Steven Meisel** |#| Name=Peter Lindbergh
			}}

			Following paragraph.
			""";
		IDocument Document = new HtmlParser().ParseDocument(Markdown.ToHtml(Source, Factory.GetPipeline()));
		Assert.Single(Document.QuerySelectorAll("section.issue-credits-group-featured"));
		IHtmlCollection<IElement> Details = Document.QuerySelectorAll("details");
		Assert.Equal(2, Details.Length);
		Assert.False(Details[0].HasAttribute("open"));
		Assert.True(Details[1].HasAttribute("open"));
		Assert.All(Details, d => Assert.Equal("SUMMARY", d.FirstElementChild!.TagName));
		Assert.Equal("/anna-wintour", Document.QuerySelector(".issue-credits-name a")!.GetAttribute("href"));
		Assert.Equal(5, Document.QuerySelectorAll(".issue-credits-name").Length);
		Assert.Equal("Steven Meisel", Document.QuerySelector(".issue-credits-name strong")!.TextContent);
		Assert.Equal("Following paragraph.", Document.QuerySelector("body > p")!.TextContent);
	}

	/// <summary>
	/// Ensures contributor links retain the site's external-link policy and raw HTML is inert.
	/// </summary>
	[Fact]
	public void NamesEncodeHtmlAndApplyLinkPolicy()
	{
		string Html = Markdown.ToHtml(
			"{{IssueCredits Group=<script>Bad</script> |~| Role=<b>Role</b> |#| Name=<script>alert(1)</script> |#| Name=[External](https://unapproved.example/person) |#| Name=[Unsafe](javascript:alert%281%29)}}",
			Factory.GetPipeline());
		IDocument Document = new HtmlParser().ParseDocument(Html);
		Assert.Empty(Document.QuerySelectorAll("script, b"));
		Assert.Contains("&lt;script&gt;", Html, StringComparison.Ordinal);
		Assert.Equal("nofollow noopener", Document.QuerySelector("a[href^='https:']")!.GetAttribute("rel"));
		Assert.Empty(Document.QuerySelectorAll("a[href^='javascript:']"));
	}

	/// <summary>
	/// Ensures invalid directives stay visible instead of silently dropping contributor data.
	/// </summary>
	/// <param name="Source">Malformed or incomplete source.</param>
	[Theory]
	[InlineData("{{IssueCredits Group=Empty}}")]
	[InlineData("{{IssueCredits Name=No group}}")]
	[InlineData("{{IssueCredits Group=Test |#| Open=yes |~| Name=Person}}")]
	[InlineData("{{IssueCredits Group=Test |~| Nmae=Person}}")]
	[InlineData("{{IssueCredits Group=Test |~| Name=Person |#| Name=}}")]
	[InlineData("{{IssueCredits Group=Test |~| Name=Person}} trailing text")]
	[InlineData("{{IssueCredits\nGroup=Test\n|~| Name=Person")]
	public void MalformedSourceRemainsVisible(string Source)
	{
		string Html = Markdown.ToHtml(Source, Factory.GetPipeline());
		Assert.DoesNotContain("<aside", Html, StringComparison.Ordinal);
		Assert.Contains("{{IssueCredits", Html, StringComparison.Ordinal);
	}

	/// <summary>
	/// Checks parser boundaries and the restricted talk pipeline.
	/// </summary>
	[Fact]
	public void OnlyStandaloneArticleDirectivesRender()
	{
		const string Source = "{{IssueCredits Group=Models |~| Name=One |#| Name=Two}}";
		Assert.Contains("<aside", Markdown.ToHtml(Source, Factory.GetPipeline()), StringComparison.Ordinal);
		Assert.DoesNotContain("<aside", Factory.RenderTalkMarkdown(Source), StringComparison.Ordinal);
		Assert.DoesNotContain("<aside", Markdown.ToHtml("```\n" + Source + "\n```", Factory.GetPipeline()), StringComparison.Ordinal);
		Assert.DoesNotContain("<aside", Markdown.ToHtml(Source.Replace("IssueCredits", "IssueCreditsExtra", StringComparison.Ordinal), Factory.GetPipeline()), StringComparison.Ordinal);
		Assert.DoesNotContain("<aside", Markdown.ToHtml("    " + Source, Factory.GetPipeline()), StringComparison.Ordinal);
	}
}
