using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
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

/// <summary>
/// Verifies Unicode-preserving heading identifiers, generated <c>Footnotes</c>/<c>Citations</c> section IDs,
/// trailing <c>h2</c> section anchor links, and profile-preview suppression.
/// </summary>
public sealed class HeadingAnchorTests
{
	/// <summary>
	/// Verifies native-script headings keep their Unicode identifiers across Japanese, Arabic, and accented Latin text,
	/// with sequential suffixes on duplicates and explicit generic-attribute overrides preserved.
	/// </summary>
	/// <returns>Completion after rendered DOM inspection.</returns>
	[Fact]
	public async Task PreservesUnicodeHeadingIdentifiersAndExplicitOverrides()
	{
		const string Source = """
			## 歴史

			## التاريخ

			## Édition française

			## 歴史

			## Archive holdings {#custom-holdings}
			""";

		using IDocument Document = await RenderFullArticleAsync(Source);
		IElement[] Headings = [.. Document.QuerySelectorAll("h2")];
		Assert.Equal(["歴史", "التاريخ", "édition-française", "歴史-1", "custom-holdings"], Headings.Select(h => h.Id!).ToArray());

		string[] AnchorHrefs = [.. Document.QuerySelectorAll("h2 > a.heading-anchor").Select(a => a.GetAttribute("href")!)];
		Assert.Equal(["#歴史", "#التاريخ", "#édition-française", "#歴史-1", "#custom-holdings"], AnchorHrefs);
	}

	/// <summary>
	/// Verifies only top-level <c>h2</c> headings and generated reference sections emit section anchor links,
	/// leaving <c>h3</c>, <c>h4</c>, blockquotes, custom containers, and component subheads untouched.
	/// </summary>
	/// <returns>Completion after rendered DOM inspection.</returns>
	[Fact]
	public async Task EmitsAnchorsOnlyOnTopLevelH2AndGeneratedReferenceSections()
	{
		const string Source = """
			## Publication history

			### 1980s: the Petersen years

			#### Quarterly spin-off

			> ## Quoted heading

			:::sidebar
			## Sidebar heading
			:::

			{{IssueCredits Group=Editorial |#| Featured=true |~| Role=Editor |#| Name=Jane Doe}}

			Body note.{{Footnote Supporting note.}} Fact.{{Citation title=Reference |#| year=2024}}

			{{Footnotes}}

			{{Citations}}
			""";

		using IDocument Document = await RenderFullArticleAsync(Source);
		IElement[] Anchors = [.. Document.QuerySelectorAll("a.heading-anchor")];
		Assert.Equal(
			["#publication-history", "#footnotes", "#citations"],
			Anchors.Select(a => a.GetAttribute("href")!).ToArray());

		Assert.Equal(
			["Link to section: Publication history", "Link to section: Footnotes", "Link to section: Citations"],
			Anchors.Select(a => a.GetAttribute("aria-label")!).ToArray());

		IElement Subheading = Document.QuerySelector("h3")!;
		Assert.Equal("s-the-petersen-years", Subheading.Id);
		Assert.Null(Subheading.QuerySelector("a.heading-anchor"));
		Assert.Null(Document.QuerySelector("blockquote a.heading-anchor"));
		Assert.Null(Document.QuerySelector(".sidebar a.heading-anchor"));
		Assert.Null(Document.QuerySelector(".issue-credits a.heading-anchor"));
	}

	/// <summary>
	/// Verifies headings containing inline links, emphasis, code, or citation/footnote markers keep valid non-nested HTML
	/// and clean plain-text accessible labels.
	/// </summary>
	/// <returns>Completion after rendered DOM inspection.</returns>
	[Fact]
	public async Task FormatsAccessibleLabelWithoutInlineMarkupOrReferenceSuperscripts()
	{
		const string Source = """
			## The [*GQ*](/gq) & `Wired` years{{Citation title=Ref |#| year=2020}}{{Footnote Extra detail.}}

			{{Footnotes}}

			{{Citations}}
			""";

		using IDocument Document = await RenderFullArticleAsync(Source, Prettify: true);
		IElement Heading = Document.QuerySelector("h2")!;
		IElement[] LinksInHeading = [.. Heading.QuerySelectorAll("a")];

		// Inline article link, citation superscript link, footnote superscript link, plus the trailing heading-anchor sibling: none nested.
		Assert.Equal(4, LinksInHeading.Length);
		Assert.All(LinksInHeading, Link => Assert.Null(Link.ParentElement?.Closest("a")));

		IElement Anchor = Heading.QuerySelector("a.heading-anchor")!;
		Assert.Equal("Link to section: The GQ & Wired years", Anchor.GetAttribute("aria-label"));
		Assert.Empty(Anchor.TextContent);
	}

	/// <summary>
	/// Verifies <c>{{Footnotes}}</c> and <c>{{Citations}}</c> deduplicate their heading IDs when authored headings
	/// already claim <c>footnotes</c> or <c>citations</c>.
	/// </summary>
	/// <returns>Completion after rendered DOM inspection.</returns>
	[Fact]
	public async Task DeduplicatesReferenceHeadingIdsAgainstAuthoredHeadings()
	{
		const string Source = """
			## Footnotes

			## Citations

			Body.{{Footnote Note body.}} Claim.{{Citation title=Source |#| year=2025}}

			{{Footnotes}}

			{{Citations}}
			""";

		using IDocument Document = await RenderFullArticleAsync(Source);
		string[] AllIds = [.. Document.All.Where(e => !string.IsNullOrEmpty(e.Id)).Select(e => e.Id!)];
		Assert.Equal(AllIds.Length, AllIds.Distinct(StringComparer.Ordinal).Count());

		Assert.Equal("footnotes-1", Document.QuerySelector(".footnotes > h2")!.Id);
		Assert.Equal("#footnotes-1", Document.QuerySelector(".footnotes > h2 > a.heading-anchor")!.GetAttribute("href"));
		Assert.Equal("citations-1", Document.QuerySelector(".citations > h2")!.Id);
		Assert.Equal("#citations-1", Document.QuerySelector(".citations > h2 > a.heading-anchor")!.GetAttribute("href"));
	}

	/// <summary>
	/// Verifies embedded profile renders (<c>IncludeHeadingAnchors: false</c>) keep heading IDs while omitting anchor links.
	/// </summary>
	/// <returns>Completion after rendered DOM inspection.</returns>
	[Fact]
	public async Task OmitsAnchorLinksWhenDisabledForEmbeddedProfilePreview()
	{
		const string Source = """
			## Collection

			Note.{{Footnote Owned issues.}}

			{{Footnotes}}
			""";

		using IDocument Document = await RenderFullArticleAsync(Source, IncludeHeadingAnchors: false);
		Assert.NotNull(Document.GetElementById("collection"));
		Assert.NotNull(Document.GetElementById("footnotes"));
		Assert.Empty(Document.QuerySelectorAll("a.heading-anchor"));
	}

	/// <summary>
	/// Verifies <see cref="RedirectExtension.TryExtractTarget"/> accepts Unicode section fragments matching Unicode heading IDs.
	/// </summary>
	[Fact]
	public void RedirectAcceptsUnicodeFragmentsAndRejectsUnsafeCharacters()
	{
		Assert.True(RedirectExtension.TryExtractTarget("{{Redirect /popeye#歴史}}", out string SlugJa, out string? FragJa));
		Assert.Equal("popeye", SlugJa);
		Assert.Equal("歴史", FragJa);

		Assert.True(RedirectExtension.TryExtractTarget("{{Redirect /majalla#التاريخ}}", out string SlugAr, out string? FragAr));
		Assert.Equal("majalla", SlugAr);
		Assert.Equal("التاريخ", FragAr);

		Assert.True(RedirectExtension.TryExtractTarget("{{Redirect /magazine#e\u0301dition}}", out _, out string? FragCombining));
		Assert.Equal("e\u0301dition", FragCombining);

		Assert.False(RedirectExtension.TryExtractTarget("{{Redirect /magazine#bad fragment}}", out _, out _));
		Assert.False(RedirectExtension.TryExtractTarget("{{Redirect /magazine#<script>}}", out _, out _));
	}

	/// <summary>
	/// Renders synthetic Markdown through <see cref="ArticleRenderer"/> and optionally <see cref="HtmlPrettifyMiddleware"/>.
	/// </summary>
	/// <param name="Source">Markdown source text.</param>
	/// <param name="IncludeHeadingAnchors">Whether section anchor links are enabled.</param>
	/// <param name="Prettify">Whether to pass the rendered HTML through <see cref="HtmlPrettifyMiddleware"/>.</param>
	/// <returns>A parsed HTML document for DOM assertions.</returns>
	private static async Task<IDocument> RenderFullArticleAsync(
		string Source,
		bool IncludeHeadingAnchors = true,
		bool Prettify = false)
	{
		IOptions<SiteConfiguration> Configuration = Options.Create(new SiteConfiguration());
		await using WikiWikiWorldDbContext Context = new(
			new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite("Data Source=:memory:").Options);
		ArticleRenderer Renderer = new(
			Context,
			new MarkdownPipelineFactory(new TrustedLinkDomainList()),
			Configuration,
			TestInfoboxSchemas.Catalog);
		ArticleRevision Revision = new()
		{
			CanonicalArticleId = Guid.NewGuid(),
			SiteId = 1,
			Culture = "en",
			Title = "Heading anchor test",
			UrlSlug = "heading-anchor-test",
			RevisionReason = "Test fixture",
			Text = Source
		};

		RenderedArticle Rendered = await Renderer.RenderAsync(
			Revision,
			SiteId: 1,
			Culture: "en",
			EditUrl: "/heading-anchor-test/edit",
			CancellationToken: default,
			IncludeHeadingAnchors: IncludeHeadingAnchors);

		string Html = "<article data-ignore=\"true\">" + Rendered.Html + "</article>";
		if (Prettify)
		{
			using MemoryStream Output = new();
			DefaultHttpContext Http = new();
			Http.Response.Body = Output;
			HtmlPrettifyMiddleware Middleware = new(async Request =>
			{
				Request.Response.ContentType = "text/html; charset=utf-8";
				await Request.Response.WriteAsync("<!doctype html><html><body>" + Html + "</body></html>");
			});
			await Middleware.Invoke(Http);
			Html = Encoding.UTF8.GetString(Output.ToArray());
		}

		return new HtmlParser().ParseDocument(Html);
	}
}
