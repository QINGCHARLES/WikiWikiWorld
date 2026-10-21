using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Verifies issue contents preserve authored data, reject malformed input, and never create links.</summary>
public sealed class IssueContentsTests
{
	private static readonly MarkdownPipelineFactory Factory = new(Options.Create(new SiteConfiguration()));

	/// <summary>Checks grouping, optional fields, heading hierarchy, source order, and the closing boundary.</summary>
	[Fact]
	public void SectionsPreservePrintedContents()
	{
		const string Source = """
			{{IssueContents
			Group=Features |#| Featured=true
			|~| Title=Wild at Heart |#| Description=The Trinity in the fall collections |#| Credit=Photographs by Steven Meisel |#| Pages=120–143
			|~| Title=American Anthem
			|~| Group=Fashion & Beauty
			|~| Title=Bare Essentials |#| Pages=iv
			|~| Group=Regular Features
			|~| Title=Last Look |#| Description=The back page
			}}

			Following paragraph.
			""";
		IDocument Document = new HtmlParser().ParseDocument(Markdown.ToHtml(Source, Factory.GetPipeline()));
		Assert.Equal(3, Document.QuerySelectorAll(".issue-contents-group").Length);
		Assert.Single(Document.QuerySelectorAll(".issue-contents-featured"));
		Assert.Equal(["Wild at Heart", "American Anthem", "Bare Essentials", "Last Look"],
			Document.QuerySelectorAll("h4.issue-contents-title").Select(x => x.TextContent));
		Assert.Equal("Photographs by Steven Meisel", Document.QuerySelector(".issue-contents-credit")!.TextContent);
		Assert.Equal(["p. 120–143", "p. iv"], Document.QuerySelectorAll(".issue-contents-pages").Select(x => x.TextContent));
		Assert.Equal(2, Document.QuerySelectorAll(".issue-contents-description").Length);
		Assert.Equal("Following paragraph.", Document.QuerySelector("body > p")!.TextContent);
		Assert.Empty(Document.QuerySelectorAll("a, button, details, nav"));
	}

	/// <summary>Allows title-only entries and case-insensitive properties without inventing group labels.</summary>
	[Fact]
	public void TitlesAloneAndRepeatedTitlesAreValid()
	{
		IDocument Document = new HtmlParser().ParseDocument(Markdown.ToHtml(
			"{{IssueContents title=One |~| TITLE=One |~| Group=Later |#| featured=TRUE |~| Title=Two}}", Factory.GetPipeline()));
		Assert.Equal(2, Document.QuerySelectorAll("h3.issue-contents-title").Length);
		Assert.Single(Document.QuerySelectorAll(".issue-contents-group-title"));
		Assert.Equal(2, Document.QuerySelectorAll(".issue-contents-featured").Length);
		Assert.Empty(Document.QuerySelectorAll(".issue-contents-description, .issue-contents-credit, .issue-contents-pages"));
	}

	/// <summary>All authored fields remain inert, including HTML, Markdown links, and literal equals signs.</summary>
	[Fact]
	public void EveryFieldIsPlainText()
	{
		IDocument Document = new HtmlParser().ParseDocument(Markdown.ToHtml(
			"{{IssueContents Group=<b>Group</b> |~| Title=[Link](https://example.com) |#| Description=<script>alert(1)</script> |#| Credit=A=B & C |#| Pages=<img src=x onerror=alert(1)>}}", Factory.GetPipeline()));
		Assert.Empty(Document.QuerySelectorAll("a, script, b, img"));
		Assert.Equal("[Link](https://example.com)", Document.QuerySelector(".issue-contents-title")!.TextContent);
		Assert.Equal("<script>alert(1)</script>", Document.QuerySelector(".issue-contents-description")!.TextContent);
		Assert.Equal("A=B & C", Document.QuerySelector(".issue-contents-credit")!.TextContent);
	}

	/// <summary>Invalid entries cannot silently remove or alter magazine contents.</summary>
	/// <param name="Source">An invalid directive whose full source must remain visible.</param>
	[Theory]
	[InlineData("{{IssueContents}}")]
	[InlineData("{{IssueContents Group=Empty}}")]
	[InlineData("{{IssueContents Group=A |~| Group=B |~| Title=C}}")]
	[InlineData("{{IssueContents Description=No title}}")]
	[InlineData("{{IssueContents Title=}}")]
	[InlineData("{{IssueContents Title=A |#| title=B}}")]
	[InlineData("{{IssueContents Title=A |#| Pages=}}")]
	[InlineData("{{IssueContents Title=A |#| Link=/a}}")]
	[InlineData("{{IssueContents Group=A |#| Featured=yes |~| Title=B}}")]
	[InlineData("{{IssueContents Group=A |#| Title=B}}")]
	[InlineData("{{IssueContents Title=A |~|}}")]
	[InlineData("{{IssueContents Title={{Stub}}}}")]
	[InlineData("{{IssueContents Title=A}} trailing")]
	[InlineData("{{IssueContents\nTitle=A")]
	public void MalformedSourceRemainsVisible(string Source)
	{
		IDocument Document = new HtmlParser().ParseDocument(Markdown.ToHtml(Source, Factory.GetPipeline()));
		Assert.Empty(Document.QuerySelectorAll(".issue-contents"));
		Assert.Equal(Source, Document.QuerySelector(".issue-contents-error")!.TextContent);
	}

	/// <summary>Only standalone article directives render; code samples and talk posts retain literal source.</summary>
	[Fact]
	public void ParserRespectsArticleAndCodeBoundaries()
	{
		const string Source = "{{IssueContents Title=Example}}";
		Assert.Contains("class=\"issue-contents\"", Markdown.ToHtml(Source, Factory.GetPipeline()), StringComparison.Ordinal);
		Assert.DoesNotContain("class=\"issue-contents\"", Factory.RenderTalkMarkdown(Source), StringComparison.Ordinal);
		foreach (string Literal in new[] { "```\n" + Source + "\n```", "    " + Source,
			"Inline " + Source, Source.Replace("IssueContents", "IssueContentsExtra", StringComparison.Ordinal) })
		{
			Assert.DoesNotContain("class=\"issue-contents\"", Markdown.ToHtml(Literal, Factory.GetPipeline()), StringComparison.Ordinal);
		}
	}
}
