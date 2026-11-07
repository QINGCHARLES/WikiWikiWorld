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

/// <summary>Checks Markdown attribute and destination safety across the actual article, talk and nested rendering paths.</summary>
public sealed class MarkdownSafetyTests
{
	/// <summary>All demonstrated native-event injection locations must lose their handlers without losing their content.</summary>
	/// <param name="Source">Synthetic Markdown using generic HTML attributes.</param>
	[Theory]
	[InlineData("Click this paragraph.{onclick=\"alert(1)\"}")]
	[InlineData("## Click heading {onclick=\"alert(1)\"}")]
	[InlineData("[Click link](#probe){onclick=\"alert(1)\"}")]
	[InlineData("![Image](/probe.png){onerror=\"alert(1)\"}")]
	[InlineData("![Image](/probe.png){onload=\"alert(1)\"}")]
	[InlineData("Click ::this span::{onclick=\"alert(1)\"}.")]
	[InlineData(":::note {onpointerenter=\"alert(1)\"}\nContainer body.\n:::")]
	[InlineData("```text {onclick=\"alert(1)\"}\nClick this code.\n```")]
	[InlineData("{onclick=\"alert(1)\"}\n\nClick next paragraph.")]
	[InlineData("Read.{{Footnote [Click note](#probe){onclick=\"alert(1)\"} .}}\n\n{{Footnotes}}")]
	[InlineData("{{PublicationIssueInfobox Editor=[Click editor](#probe){onclick=\"alert(1)\"} |#| CoverDate=2025}}")]
	[InlineData("[Click link](#probe){OnClIcK=\"alert(1)\"}")]
	[InlineData("[Click](#probe){onbeforetoggle=\"alert(1)\" onanimationstart=\"alert(1)\"}")]
	public void RemovesNativeEventAttributes(string Source)
	{
		using IDocument Document = new HtmlParser().ParseDocument(Render(Source));
		Assert.DoesNotContain(Document.All.SelectMany(e => e.Attributes), a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase));
		Assert.NotEmpty(Document.Body!.TextContent);
	}

	/// <summary>Executable/framework and destination overrides are removed while the renderer's original links and policy remain.</summary>
	[Fact]
	public void RejectsReservedAttributesAndRecomputesRel()
	{
		string Source = "[Archive](https://unapproved.test/article){href=\"javascript:alert(1)\" rel=opener target=_blank style=\"position:fixed\" data-on:click=\"alert(1)\" data-ignore=false data-signals=\"alert(1)\" srcdoc=\"<script>alert(1)</script>\"}";
		using IDocument Document = new HtmlParser().ParseDocument(Render(Source));
		IElement Link = Assert.Single(Document.QuerySelectorAll("a"));
		Assert.Equal("https://unapproved.test/article", Link.GetAttribute("href"));
		Assert.Equal("nofollow noopener", Link.GetAttribute("rel"));
		Assert.Equal(["href", "rel"], Link.Attributes.Select(a => a.Name).Order(StringComparer.Ordinal).ToArray());
	}

	/// <summary>Documented IDs/classes and inert presentation/accessibility metadata remain usable.</summary>
	[Fact]
	public void PreservesSafeFormattingAndGeneratedAnchors()
	{
		string Source = "## Automatic heading\n\n## Archive holdings {#holdings .catalog-section}\n\n[Archive](#holdings){.external data-kind=primary-source title=\"A & B\" aria-label=\"Archive link\" role=link lang=en dir=ltr}\n\n:::sidebar {#collectors-note data-kind=archive}\nNote.\n:::\n\n![Cover](/cover.jpg){width=320 height=480}";
		using IDocument Document = new HtmlParser().ParseDocument(Render(Source));
		Assert.NotNull(Document.GetElementById("automatic-heading"));
		Assert.Contains("catalog-section", Document.GetElementById("holdings")!.ClassList);
		IElement Link = Assert.Single(Document.QuerySelectorAll("a"));
		Assert.Equal("primary-source", Link.GetAttribute("data-kind"));
		Assert.Equal("A & B", Link.GetAttribute("title"));
		Assert.Equal("Archive link", Link.GetAttribute("aria-label"));
		Assert.Equal("link", Link.GetAttribute("role"));
		Assert.Equal("en", Link.GetAttribute("lang"));
		Assert.Equal("ltr", Link.GetAttribute("dir"));
		Assert.Equal("archive", Document.GetElementById("collectors-note")!.GetAttribute("data-kind"));
		Assert.Equal("320", Document.QuerySelector("img")!.GetAttribute("width"));
		Assert.Equal("480", Document.QuerySelector("img")!.GetAttribute("height"));
	}

	/// <summary>Destination checks reject executable, obfuscated and unsupported schemes in articles and private/talk bodies.</summary>
	/// <param name="Url">An unsafe destination as authored in Markdown.</param>
	[Theory]
	[InlineData("javascript:alert(1)")]
	[InlineData("JaVaScRiPt:alert(1)")]
	[InlineData("java&#x73;cript:alert(1)")]
	[InlineData("javascript&#58;alert(1)")]
	[InlineData("java&#x09;script:alert(1)")]
	[InlineData("data:text/html,probe")]
	[InlineData("vbscript:msgbox(1)")]
	[InlineData("file:///tmp/probe.html")]
	[InlineData("custom:probe")]
	public void RejectsUnsafeDestinationsInBothPipelines(string Url)
	{
		foreach (bool Talk in new[] { false, true })
		{
			using IDocument Document = new HtmlParser().ParseDocument(Render("[Visible link](" + Url + ")\n\n![Visible image](" + Url + ")", Talk));
			Assert.Equal(string.Empty, Document.QuerySelector("a")!.GetAttribute("href"));
			Assert.Equal("Visible link", Document.QuerySelector("a")!.TextContent);
			Assert.Equal(string.Empty, Document.QuerySelector("img")!.GetAttribute("src"));
			Assert.Equal("Visible image", Document.QuerySelector("img")!.GetAttribute("alt"));
		}
	}

	/// <summary>Normal internal, web and documented non-executable link destinations retain their original meaning.</summary>
	/// <param name="Url">A supported destination.</param>
	[Theory]
	[InlineData("/archive")]
	[InlineData("../archive")]
	[InlineData("#holdings")]
	[InlineData("//example.test/archive")]
	[InlineData("https://example.test/archive?a=1&b=2")]
	[InlineData("http://example.test/archive")]
	[InlineData("ftp://example.test/archive")]
	[InlineData("mailto:editor@example.test")]
	[InlineData("tel:+123456789")]
	public void PreservesSupportedLinks(string Url)
	{
		foreach (bool Talk in new[] { false, true })
		{
			using IDocument Document = new HtmlParser().ParseDocument(Render("[Archive](" + Url + ")", Talk));
			Assert.Equal(Url, Document.QuerySelector("a")!.GetAttribute("href"));
		}
	}

	/// <summary>Images retain internal/HTTP sources but cannot turn navigation-only or data schemes into image sources.</summary>
	/// <param name="Url">Image destination.</param>
	/// <param name="Expected">Allowed source, or empty for rejected input.</param>
	[Theory]
	[InlineData("/cover.jpg", "/cover.jpg")]
	[InlineData("https://example.test/cover.jpg", "https://example.test/cover.jpg")]
	[InlineData("mailto:editor@example.test", "")]
	[InlineData("ftp://example.test/cover.jpg", "")]
	[InlineData("data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7", "")]
	public void AppliesImageDestinationPolicy(string Url, string Expected)
	{
		foreach (bool Talk in new[] { false, true })
		{
			using IDocument Document = new HtmlParser().ParseDocument(Render("![Cover](" + Url + ")", Talk));
			Assert.Equal(Expected, Document.QuerySelector("img")!.GetAttribute("src"));
		}
	}

	/// <summary>Late custom-note parsing uses the same safety policy as surrounding text and infobox facts.</summary>
	[Fact]
	public void RestrictsNestedDestinations()
	{
		const string Link = "[Visible](javascript:alert(1))";
		string Source = "{{PublicationIssueInfobox Editor=" + Link + " |#| CoverDate=2025}}\n\nRead.{{Footnote " + Link + ".}}\n\n{{Footnotes}}";
		using IDocument Document = new HtmlParser().ParseDocument(Render(Source));
		IElement[] UnsafeLinks = [.. Document.QuerySelectorAll("a").Where(a => a.TextContent == "Visible")];
		Assert.Equal(2, UnsafeLinks.Length);
		Assert.All(UnsafeLinks, a => Assert.Equal(string.Empty, a.GetAttribute("href")));
	}

	/// <summary>Attribute syntax inside code remains literal, and talk Markdown does not gain the generic-attribute feature.</summary>
	[Fact]
	public void PreservesLiteralCodeAndMinimalTalkBehavior()
	{
		const string Literal = "[Link](#probe){onclick=\"alert(1)\"}";
		using IDocument Code = new HtmlParser().ParseDocument(Render("```\n" + Literal + "\n```"));
		Assert.Contains(Literal, Code.QuerySelector("code")!.TextContent);
		Assert.Null(Code.QuerySelector("[onclick], a"));
		using IDocument Talk = new HtmlParser().ParseDocument(Render(Literal, true));
		Assert.Null(Talk.QuerySelector("[onclick]"));
		Assert.Contains("{onclick=", Talk.Body!.TextContent);
	}

	/// <summary>The real article renderer and final HTML middleware must preserve the policy's guarantees.</summary>
	/// <returns>Completion after the complete output path passes parsed-DOM assertions.</returns>
	[Fact]
	public async Task FullArticleAndFormattingRemainSafe()
	{
		IOptions<SiteConfiguration> Configuration = Options.Create(new SiteConfiguration());
		await using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite("Data Source=:memory:").Options);
		ArticleRenderer Renderer = new(Context, new MarkdownPipelineFactory(Configuration), Configuration);
		ArticleRevision Revision = new()
		{
			CanonicalArticleId = Guid.NewGuid(), SiteId = 1, Culture = "en", Title = "Safety test", UrlSlug = "safety-test", RevisionReason = "In-memory fixture",
			Text = "[Visible](javascript:alert(1)){onclick=\"alert(1)\"}\n\n![Image](/missing.png){onerror=\"alert(1)\"}"
		};
		RenderedArticle Article = await Renderer.RenderAsync(Revision, 1, "en", "/safety-test/edit", default);
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
		Assert.Null(Document.QuerySelector("[onclick], [onerror]"));
		Assert.Equal(string.Empty, Document.QuerySelector("a")!.GetAttribute("href"));
		Assert.Equal("/missing.png", Document.QuerySelector("img")!.GetAttribute("src"));
	}

	/// <summary>Runs the production pipeline and note passes without changing a stored article.</summary>
	/// <param name="Source">Synthetic Markdown.</param>
	/// <param name="Talk">Whether to use the minimal discussion/private-message pipeline.</param>
	/// <returns>Rendered HTML for DOM inspection.</returns>
	private static string Render(string Source, bool Talk = false)
	{
		MarkdownPipelineFactory Factory = new(Options.Create(new SiteConfiguration()));
		if (Talk) return Factory.RenderUserContentMarkdown(Source);
		MarkdownPipeline Pipeline = Factory.GetPipeline();
		MarkdownDocument Document = Markdown.Parse(Source, Pipeline);
		FootnoteExtension.ReprocessFootnotes(Document, Pipeline);
		CitationExtension.ReprocessCitations(Document);
		using StringWriter Writer = new();
		HtmlRenderer Renderer = new(Writer);
		Pipeline.Setup(Renderer);
		Renderer.Render(Document);
		return Writer.ToString();
	}
}
