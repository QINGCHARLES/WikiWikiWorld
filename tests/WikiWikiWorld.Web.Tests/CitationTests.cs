using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.MarkdigExtensions;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Verifies citation text and destinations through the shared Markdown pipeline and final HTML processing.</summary>
public sealed class CitationTests
{
	/// <summary>Every displayed field must remain literal text rather than creating executable HTML.</summary>
	/// <param name="Field">Citation field under test.</param>
	[Theory]
	[InlineData("author")]
	[InlineData("title")]
	[InlineData("journal")]
	[InlineData("publisher")]
	[InlineData("volume")]
	[InlineData("issue")]
	[InlineData("pages")]
	[InlineData("year")]
	[InlineData("doi")]
	public void EscapesEveryTextField(string Field)
	{
		const string Payload = "<svg onload=\"document.documentElement.dataset.citationProbe='field'\"></svg> & \"quoted\" ' Ω";
		Dictionary<string, string> Properties = new() { ["title"] = "Reference" };
		if (Field == "issue") Properties["volume"] = "1";
		Properties[Field] = Payload;
		string Data = string.Join(" |#| ", Properties.Select(p => p.Key + "=" + p.Value));
		using IDocument Document = new HtmlParser().ParseDocument(Render("Reading.{{Citation " + Data + "}}\n\n{{Citations}}"));
		Assert.Contains(Payload, Document.QuerySelector(".citation-item")!.TextContent);
		AssertInert(Document);
	}

	/// <summary>Quoted URL input cannot escape an href, regardless of whether the title consumes the link.</summary>
	/// <param name="WithTitle">Whether to exercise the title-link branch rather than the host-label branch.</param>
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void UrlQuotesCannotIntroduceAttributes(bool WithTitle)
	{
		const string Url = "https://example.test/\" onclick=\"document.documentElement.dataset.citationProbe='url';return false\" data-probe=\"";
		string Title = WithTitle ? "title=Reference |#| " : string.Empty;
		using IDocument Document = new HtmlParser().ParseDocument(Render("Reading.{{Citation " + Title + "url=" + Url + "}}\n\n{{Citations}}"));
		AssertInert(Document);
		Assert.Null(Document.QuerySelector("[data-probe]"));
		IElement Link = Assert.Single(Document.QuerySelectorAll(".citation-item a:not(.citation-backref)"));
		Assert.Equal(new Uri(Url).AbsoluteUri, Link.GetAttribute("href"));
		Assert.Equal(["href", "rel", "target"], Link.Attributes.Select(a => a.Name).Order(StringComparer.Ordinal).ToArray());
	}

	/// <summary>Disallowed schemes and malformed/relative destinations stay visible as inert text in both URL branches.</summary>
	/// <param name="Url">An unsupported or invalid destination.</param>
	[Theory]
	[InlineData("javascript:document.documentElement.dataset.citationProbe='scheme'")]
	[InlineData("JaVaScRiPt:alert(1)")]
	[InlineData("java\tscript:alert(1)")]
	[InlineData("javascript&#58;alert(1)")]
	[InlineData("data:text/html,<svg onload=alert(1)></svg>")]
	[InlineData("vbscript:msgbox(1)")]
	[InlineData("file:///tmp/probe.html")]
	[InlineData("ftp://example.test/file")]
	[InlineData("mailto:editor@example.test")]
	[InlineData("//example.test/article")]
	[InlineData("/relative-article")]
	[InlineData("https:///missing-host")]
	[InlineData("https://")]
	[InlineData("<svg onload=alert(1)></svg>")]
	public void UnsafeOrInvalidUrlsAreNotLinks(string Url)
	{
		foreach (bool WithTitle in new[] { true, false })
		{
			string Title = WithTitle ? "title=Reference |#| " : string.Empty;
			using IDocument Document = new HtmlParser().ParseDocument(Render("Reading.{{Citation " + Title + "url=" + Url + "}}\n\n{{Citations}}"));
			Assert.Empty(Document.QuerySelectorAll(".citation-item a:not(.citation-backref)"));
			Assert.Contains(Url, Document.QuerySelector(".citation-item")!.TextContent);
			if (WithTitle) Assert.Equal("Reference", Document.QuerySelector(".citation-item em")!.TextContent);
			AssertInert(Document);
		}
	}

	/// <summary>Safe URLs keep query values intact while applying HTML encoding exactly once.</summary>
	/// <param name="WithTitle">The title or standalone URL branch.</param>
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void SafeUrlsPreserveQueryValuesAndHostLabels(bool WithTitle)
	{
		const string Url = "https://www.example.test/search?q=%22A%22&lang=ja";
		string Title = WithTitle ? "title=A & B |#| " : string.Empty;
		string Html = Render("Reading.{{Citation " + Title + "url=" + Url + "}}\n\n{{Citations}}");
		using IDocument Document = new HtmlParser().ParseDocument(Html);
		IElement Link = Assert.Single(Document.QuerySelectorAll(".citation-item a:not(.citation-backref)"));
		Assert.Equal(Url, Link.GetAttribute("href"));
		Assert.Equal(WithTitle ? "A & B" : "example.test", Link.TextContent);
		Assert.Contains("&amp;lang=ja", Html);
		Assert.DoesNotContain("&amp;amp;", Html);
	}

	/// <summary>Domain approval affects nofollow without bypassing destination safety or removing noopener.</summary>
	/// <param name="Url">A valid external citation URL.</param>
	/// <param name="ExpectedRel">The established domain policy result.</param>
	[Theory]
	[InlineData("https://approved.test/article", "noopener")]
	[InlineData("https://sub.approved.test/article", "noopener")]
	[InlineData("http://approved.test/article", "noopener")]
	[InlineData("https://unapproved.test/article", "nofollow noopener")]
	[InlineData("https://approved.test.evil.test/article", "nofollow noopener")]
	public void PreservesApprovedDomainPolicy(string Url, string ExpectedRel)
	{
		using IDocument Document = new HtmlParser().ParseDocument(Render("Reading.{{Citation title=Reference |#| url=" + Url + "}}\n\n{{Citations}}", ["approved.test"]));
		IElement Link = Assert.Single(Document.QuerySelectorAll(".citation-item a:not(.citation-backref)"));
		Assert.Equal(ExpectedRel, Link.GetAttribute("rel"));
		Assert.Equal("_blank", Link.GetAttribute("target"));
	}

	/// <summary>Reserved characters in a DOI remain part of its identifier and cannot introduce query/fragment syntax or markup.</summary>
	[Fact]
	public void EncodesDoiIdentifierAndLabelIndependently()
	{
		const string Doi = "10.1000/日本語/a?x=1#section&extra=\"quoted\"<svg onload=alert(1)>";
		using IDocument Document = new HtmlParser().ParseDocument(Render("Reading.{{Citation doi=" + Doi + "}}\n\n{{Citations}}", ["doi.org"]));
		IElement Link = Assert.Single(Document.QuerySelectorAll(".citation-item a:not(.citation-backref)"));
		Uri Target = new(Link.GetAttribute("href")!);
		Assert.Equal("https", Target.Scheme);
		Assert.Equal("doi.org", Target.Host);
		Assert.Empty(Target.Query);
		Assert.Empty(Target.Fragment);
		Assert.Equal("/" + Doi, Uri.UnescapeDataString(Target.AbsolutePath));
		Assert.Equal(Doi, Link.TextContent);
		Assert.Equal("noopener", Link.GetAttribute("rel"));
		AssertInert(Document);
	}

	/// <summary>Normal bibliographies retain author order, typography, punctuation, duplicate handling and working backreferences.</summary>
	[Fact]
	public void PreservesFormattingAndDeduplicatedBackreferences()
	{
		const string Citation = "{{Citation author=Jane & Co |#| author=Óscar <Editor> |#| author=O'Reilly |#| title=\"A & B\" |#| journal=Journál |#| publisher=Unused publisher |#| volume=4 |#| issue=2 |#| pages=10–18 |#| year=2025 |#| doi=10.1000/example |#| url=https://example.test/article}}";
		using IDocument Document = new HtmlParser().ParseDocument(Render("First." + Citation + " Again." + Citation + "\n\n{{Citations}}"));
		IElement Item = Assert.Single(Document.QuerySelectorAll(".citation-item"));
		Assert.Contains("Jane & Co, Óscar <Editor> and O'Reilly. \"A & B\". Journál, vol. 4, no. 2, pp. 10–18, 2025.", Item.TextContent);
		Assert.DoesNotContain("Unused publisher", Item.TextContent);
		Assert.Equal(["\"A & B\"", "Journál"], Item.QuerySelectorAll("em").Select(e => e.TextContent).ToArray());
		Assert.Equal("https://doi.org/10.1000/example", Item.QuerySelectorAll("a")[1].GetAttribute("href"));
		Assert.Equal(2, Document.QuerySelectorAll("a[href='#cit:1']").Length);
		Assert.Equal(["#citref:1-1", "#citref:1-2"], Item.QuerySelectorAll(".citation-backref").Select(a => a.GetAttribute("href")!).ToArray());
		foreach (IElement Link in Item.QuerySelectorAll(".citation-backref")) Assert.NotNull(Document.GetElementById(Link.GetAttribute("href")![1..]));
		AssertInert(Document);
	}

	/// <summary>Citations embedded in infoboxes and custom footnotes share the safe renderer and numbering pass.</summary>
	[Fact]
	public void EscapesCitationsInInfoboxesAndFootnotes()
	{
		const string Citation = "{{Citation title=<svg onload=alert(1)></svg> |#| year=2025}}";
		string Source = "Before." + Citation + "\n\n{{PublicationIssueInfobox Editor=Jane" + Citation + "}}\n\nNote.{{Footnote Source " + Citation + ".}}\n\n{{Footnotes}}\n\n{{Citations}}";
		using IDocument Document = new HtmlParser().ParseDocument(Render(Source));
		Assert.Single(Document.QuerySelectorAll(".citation-item"));
		Assert.Equal(3, Document.QuerySelectorAll("a[href='#cit:1']").Length);
		Assert.Equal("<svg onload=alert(1)></svg>", Document.QuerySelector(".citation-item em")!.TextContent);
		AssertInert(Document);
	}

	/// <summary>Final page formatting must not reactivate citation text already escaped by the article renderer.</summary>
	/// <returns>Completion after the actual article and formatting middleware output passes DOM checks.</returns>
	[Fact]
	public async Task FullArticleAndPrettificationKeepPayloadInert()
	{
		const string Payload = "<svg onload=\"document.documentElement.dataset.citationProbe='integration'\"></svg>";
		IOptions<SiteConfiguration> Configuration = Options.Create(new SiteConfiguration());
		await using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite("Data Source=:memory:").Options);
		ArticleRenderer Renderer = new(Context, new MarkdownPipelineFactory(Configuration), Configuration);
		ArticleRevision Revision = new()
		{
			CanonicalArticleId = Guid.NewGuid(), SiteId = 1, Culture = "en", Title = "Citation test", UrlSlug = "citation-test",
			Text = "Reading.{{Citation author=" + Payload + " |#| title=Reference}}\n\n{{Citations}}", RevisionReason = "Test fixture"
		};
		RenderedArticle Article = await Renderer.RenderAsync(Revision, 1, "en", "/citation-test/edit", default);
		using MemoryStream Output = new();
		DefaultHttpContext Http = new();
		Http.Response.Body = Output;
		HtmlPrettifyMiddleware Middleware = new(async Request =>
		{
			Request.Response.ContentType = "text/html; charset=utf-8";
			await Request.Response.WriteAsync("<!doctype html><html><body><article data-ignore=\"true\">" + Article.Html + "</article></body></html>");
		});
		await Middleware.Invoke(Http);
		using IDocument Document = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(Output.ToArray()));
		Assert.Contains(Payload, Document.QuerySelector(".citation-item")!.TextContent);
		AssertInert(Document);
	}

	/// <summary>Parses with the production pipeline and runs the same shared note passes as article rendering.</summary>
	/// <param name="Source">Synthetic article Markdown.</param>
	/// <param name="ApprovedDomains">Optional approved-domain configuration for rel-policy checks.</param>
	/// <returns>Rendered HTML before full-page formatting.</returns>
	private static string Render(string Source, IReadOnlyList<string>? ApprovedDomains = null)
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineFactory(Options.Create(new SiteConfiguration { ApprovedLinkDomains = [.. ApprovedDomains ?? []] })).GetPipeline();
		MarkdownDocument Document = Markdown.Parse(Source, Pipeline);
		FootnoteExtension.ReprocessFootnotes(Document, Pipeline);
		CitationExtension.ReprocessCitations(Document);
		using StringWriter Writer = new();
		HtmlRenderer Renderer = new(Writer);
		Pipeline.Setup(Renderer);
		Renderer.Render(Document);
		return Writer.ToString();
	}

	/// <summary>Checks parsed DOM behavior rather than rejecting harmless attack strings displayed as literal text.</summary>
	/// <param name="Document">Rendered citation document.</param>
	private static void AssertInert(IDocument Document)
	{
		Assert.Null(Document.QuerySelector("svg, img, script, iframe, object, embed"));
		Assert.DoesNotContain(Document.All.SelectMany(e => e.Attributes), a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase));
	}
}
