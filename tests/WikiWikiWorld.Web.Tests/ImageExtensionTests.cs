using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies the <c>{{Image ...}}</c> extension's block/inline parsing, layout/size/frame/link options,
/// inline-Markdown captions and credits, security sanitization, database enrichment, and save warnings.
/// </summary>
public sealed class ImageExtensionTests
{
	/// <summary>
	/// Standalone <c>{{Image ...}}</c> lines parse as <see cref="MarkdigExtensions.ImageBlock"/> figures without a wrapping paragraph,
	/// while mid-sentence occurrences parse as <see cref="MarkdigExtensions.ImageInline"/> images without <c>&lt;figure&gt;</c>.
	/// </summary>
	[Fact]
	public async Task StandaloneRendersFigureAndInlineRendersPlainImage()
	{
		string Html = await RenderWithSampleFileAsync(
			"Opening paragraph.\n\n{{Image file:rabbit-logo}}\n\nInline {{Image file:rabbit-logo |#| Link=none}} text.");

		using IDocument Document = new HtmlParser().ParseDocument(Html);

		IElement Figure = Assert.Single(Document.QuerySelectorAll("figure.article-image"));
		Assert.Contains("article-image--center", Figure.ClassList);
		Assert.Null(Figure.Closest("p"));

		IElement FigureLink = Assert.Single(Figure.QuerySelectorAll("a.article-image__link"));
		Assert.Equal("/file:rabbit-logo", FigureLink.GetAttribute("href"));

		IElement FigureImg = Assert.Single(Figure.QuerySelectorAll("img"));
		Assert.Equal("Rabbit Logo", FigureImg.GetAttribute("alt"));
		Assert.Equal("180", FigureImg.GetAttribute("width"));
		Assert.Equal("268", FigureImg.GetAttribute("height"));
		Assert.Equal("lazy", FigureImg.GetAttribute("loading"));
		Assert.Equal("async", FigureImg.GetAttribute("decoding"));
		Assert.Empty(Figure.QuerySelectorAll("figcaption"));

		IElement InlineParagraph = Document.QuerySelectorAll("p").Single(p => p.TextContent.Contains("Inline", StringComparison.Ordinal));
		Assert.Empty(InlineParagraph.QuerySelectorAll("figure"));
		Assert.Empty(InlineParagraph.QuerySelectorAll("a"));
		IElement InlineImg = Assert.Single(InlineParagraph.QuerySelectorAll("img"));
		Assert.Equal("180", InlineImg.GetAttribute("width"));
		Assert.Equal("268", InlineImg.GetAttribute("height"));
	}

	/// <summary>
	/// Verifies every supported <c>Layout</c>, <c>Size</c>, <c>Frame</c>, and <c>Link</c> combination.
	/// </summary>
	/// <param name="Directive">The authored directive.</param>
	/// <param name="ExpectedClasses">Classes expected on the rendered figure.</param>
	/// <param name="ForbiddenClasses">Classes that must not appear on the rendered figure.</param>
	/// <param name="ExpectLink">Whether the image should be wrapped in a file-article link.</param>
	[Theory]
	[InlineData(
		"{{Image file:rabbit-logo |#| Layout=aside |#| Size=small |#| Frame=paper}}",
		new[] { "article-image", "article-image--aside", "article-image--small", "article-image--frame-paper" },
		new[] { "article-image--medium" },
		true)]
	[InlineData(
		"{{Image file:rabbit-logo |#| Layout=aside}}",
		new[] { "article-image", "article-image--aside", "article-image--medium" },
		new[] { "article-image--frame-none" },
		true)]
	[InlineData(
		"{{Image file:rabbit-logo |#| Layout=start |#| Size=large |#| Frame=dark |#| Link=none}}",
		new[] { "article-image", "article-image--start", "article-image--large", "article-image--frame-dark" },
		new[] { "article-image--medium" },
		false)]
	[InlineData(
		"{{Image file:rabbit-logo |#| Layout=full |#| Size=small |#| Frame=border}}",
		new[] { "article-image", "article-image--full", "article-image--frame-border" },
		new[] { "article-image--small" },
		true)]
	[InlineData(
		"{{Image file:rabbit-logo |#| Layout=breakout |#| Size=large}}",
		new[] { "article-image", "article-image--breakout" },
		new[] { "article-image--large" },
		true)]
	[InlineData(
		"{{Image file:rabbit-logo |#| Layout=max |#| Size=medium}}",
		new[] { "article-image", "article-image--max" },
		new[] { "article-image--medium" },
		true)]
	public async Task AppliesLayoutSizeFrameAndLinkOptions(
		string Directive,
		string[] ExpectedClasses,
		string[] ForbiddenClasses,
		bool ExpectLink)
	{
		string Html = await RenderWithSampleFileAsync(Directive);
		using IDocument Document = new HtmlParser().ParseDocument(Html);

		IElement Figure = Assert.Single(Document.QuerySelectorAll("figure.article-image"));
		foreach (string ExpectedClass in ExpectedClasses)
		{
			Assert.Contains(ExpectedClass, Figure.ClassList);
		}
		foreach (string ForbiddenClass in ForbiddenClasses)
		{
			Assert.DoesNotContain(ForbiddenClass, Figure.ClassList);
		}

		if (ExpectLink)
		{
			Assert.Single(Figure.QuerySelectorAll("a.article-image__link"));
		}
		else
		{
			Assert.Empty(Figure.QuerySelectorAll("a.article-image__link"));
		}
	}

	/// <summary>
	/// Verifies caption-only, credit-only, combined caption+credit, and inline Markdown + approved-domain link policies.
	/// </summary>
	[Fact]
	public async Task RendersCaptionAndCreditWithInlineMarkdownAndTrustedLinkPolicy()
	{
		TrustedLinkDomainList TrustedDomains = new();
		TrustedDomains.Replace(["trusted.example"]);

		string MarkdownSource = string.Join("\n\n",
		[
			"{{Image file:rabbit-logo |#| Caption=**RABBIT HEAD** — Designed for *Playboy* ([trusted](https://trusted.example/page) and [untrusted](https://other.example/page)). |#| Credit=Art Paul / [*Playboy*](/playboy)}}",
			"{{Image file:rabbit-logo |#| Caption=Caption only *here*.}}",
			"{{Image file:rabbit-logo |#| Credit=Archive credit only.}}"
		]);

		string Html = await RenderWithSampleFileAsync(MarkdownSource, TrustedDomains);
		using IDocument Document = new HtmlParser().ParseDocument(Html);

		IHtmlCollection<IElement> Figures = Document.QuerySelectorAll("figure.article-image");
		Assert.Equal(3, Figures.Length);

		// First figure: both caption and credit with formatting and link policy
		IElement FirstCaption = Assert.Single(Figures[0].QuerySelectorAll("figcaption.article-image__caption"));
		IElement CaptionSpan = Assert.Single(FirstCaption.QuerySelectorAll("span.article-image__caption-text"));
		Assert.Equal("RABBIT HEAD", Assert.Single(CaptionSpan.QuerySelectorAll("strong")).TextContent);
		Assert.Equal("Playboy", Assert.Single(CaptionSpan.QuerySelectorAll("em")).TextContent);

		IElement TrustedLink = CaptionSpan.QuerySelectorAll("a").Single(a => a.TextContent == "trusted");
		Assert.Equal("noopener", TrustedLink.GetAttribute("rel"));

		IElement UntrustedLink = CaptionSpan.QuerySelectorAll("a").Single(a => a.TextContent == "untrusted");
		Assert.Equal("nofollow noopener", UntrustedLink.GetAttribute("rel"));

		IElement CreditSpan = Assert.Single(FirstCaption.QuerySelectorAll("span.article-image__credit"));
		IElement InternalCreditLink = Assert.Single(CreditSpan.QuerySelectorAll("a"));
		Assert.Equal("/playboy", InternalCreditLink.GetAttribute("href"));
		Assert.Null(InternalCreditLink.GetAttribute("rel"));
		Assert.Equal("Playboy", Assert.Single(InternalCreditLink.QuerySelectorAll("em")).TextContent);

		// Second figure: caption only
		IElement SecondCaption = Assert.Single(Figures[1].QuerySelectorAll("figcaption.article-image__caption"));
		Assert.Single(SecondCaption.QuerySelectorAll("span.article-image__caption-text"));
		Assert.Empty(SecondCaption.QuerySelectorAll("span.article-image__credit"));

		// Third figure: credit only
		IElement ThirdCaption = Assert.Single(Figures[2].QuerySelectorAll("figcaption.article-image__caption"));
		Assert.Empty(ThirdCaption.QuerySelectorAll("span.article-image__caption-text"));
		Assert.Equal("Archive credit only.", Assert.Single(ThirdCaption.QuerySelectorAll("span.article-image__credit")).TextContent);
	}

	/// <summary>
	/// Ensures raw HTML, unsafe link schemes, and injected event attributes in <c>Caption</c> and <c>Credit</c> are sanitized.
	/// </summary>
	[Fact]
	public async Task SanitizesHtmlAndUnsafeUrlsInCaptionAndCredit()
	{
		const string Source = "{{Image file:rabbit-logo |#| Caption=<b>Raw</b> [click](javascript:alert(1)){onclick=\"alert(1)\"} |#| Credit=<script>alert(1)</script> [bad](data:text/html,x)}}";

		string Html = await RenderWithSampleFileAsync(Source);
		using IDocument Document = new HtmlParser().ParseDocument(Html);

		Assert.DoesNotContain("<script", Html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("<b>Raw</b>", Html, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", Html, StringComparison.Ordinal);
		Assert.DoesNotContain(
			Document.All.SelectMany(e => e.Attributes),
			a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase));

		foreach (IElement Link in Document.QuerySelectorAll("figcaption a"))
		{
			Assert.Equal(string.Empty, Link.GetAttribute("href"));
		}
	}

	/// <summary>
	/// Preserves backwards compatibility with legacy bare <c>|</c> property separators and case-insensitive <c>FILE:</c> prefixes,
	/// while allowing literal <c>|</c> characters inside captions and credits.
	/// </summary>
	[Fact]
	public async Task SupportsLegacyPipeSeparatorsAndLiteralPipesInCaption()
	{
		const string Source = "{{Image FILE:rabbit-logo |#| Layout=full | Alt=Legacy alt | Caption=Left | Right | Credit=Archive | Collection}}";

		string Html = await RenderWithSampleFileAsync(Source);
		using IDocument Document = new HtmlParser().ParseDocument(Html);

		IElement Figure = Assert.Single(Document.QuerySelectorAll("figure.article-image"));
		Assert.Contains("article-image--full", Figure.ClassList);
		Assert.Equal("Legacy alt", Assert.Single(Figure.QuerySelectorAll("img")).GetAttribute("alt"));
		Assert.Equal("Left | Right", Assert.Single(Figure.QuerySelectorAll(".article-image__caption-text")).TextContent);
		Assert.Equal("Archive | Collection", Assert.Single(Figure.QuerySelectorAll(".article-image__credit")).TextContent);
	}

	/// <summary>
	/// Renders the site's missing-image placeholder without a file link when the referenced slug cannot be resolved.
	/// </summary>
	[Fact]
	public async Task UnresolvedSlugRendersMissingImagePlaceholder()
	{
		string Html = await RenderWithSampleFileAsync("{{Image file:does-not-exist |#| Caption=Missing}}");
		using IDocument Document = new HtmlParser().ParseDocument(Html);

		IElement Figure = Assert.Single(Document.QuerySelectorAll("figure.article-image"));
		Assert.Empty(Figure.QuerySelectorAll("a.article-image__link"));
		IElement Img = Assert.Single(Figure.QuerySelectorAll("img"));
		Assert.Equal("/sitefiles/1/missing-image.png", Img.GetAttribute("src"));
		Assert.Equal("Missing", Assert.Single(Figure.QuerySelectorAll(".article-image__caption-text")).TextContent);
	}

	/// <summary>
	/// Verifies <c>CaptionPosition=top</c> places the caption above the image while keeping <c>Credit</c> at the bottom,
	/// falls back to bottom credit rendering when no caption exists, and supports explicit <c>CaptionPosition=bottom</c>.
	/// </summary>
	[Fact]
	public async Task SupportsCaptionPositionTopWithBottomCredit()
	{
		TrustedLinkDomainList TrustedDomains = new();
		TrustedDomains.Replace(["trusted.example"]);

		string MarkdownSource = string.Join("\n\n",
		[
			"{{Image file:rabbit-logo |#| Frame=border |#| CaptionPosition=top |#| Caption=**RABBIT HEAD** Design by Art Paul |#| Credit=Art Paul / [*Playboy*](https://trusted.example/art)}}",
			"{{Image file:rabbit-logo |#| CaptionPosition=top |#| Caption=Caption only on top.}}",
			"{{Image file:rabbit-logo |#| CaptionPosition=top |#| Credit=Credit stays at bottom.}}",
			"{{Image file:rabbit-logo |#| CaptionPosition=bottom |#| Caption=Explicit bottom. |#| Credit=Credit}}"
		]);

		string Html = await RenderWithSampleFileAsync(MarkdownSource, TrustedDomains);
		using IDocument Document = new HtmlParser().ParseDocument(Html);

		IHtmlCollection<IElement> Figures = Document.QuerySelectorAll("figure.article-image");
		Assert.Equal(4, Figures.Length);

		// 1. Top caption + bottom credit
		IElement TopWithCredit = Figures[0];
		Assert.Contains("article-image--caption-top", TopWithCredit.ClassList);
		Assert.Equal(3, TopWithCredit.Children.Length);
		IElement TopCaption = TopWithCredit.Children[0];
		Assert.Equal("figcaption", TopCaption.TagName.ToLowerInvariant());
		Assert.Contains("article-image__caption--top", TopCaption.ClassList);
		Assert.Equal("RABBIT HEAD", Assert.Single(TopCaption.QuerySelectorAll("strong")).TextContent);
		Assert.Equal("article-image__media", TopWithCredit.Children[1].ClassName);
		IElement Footer = TopWithCredit.Children[2];
		Assert.Equal("div", Footer.TagName.ToLowerInvariant());
		Assert.Equal("article-image__footer", Footer.ClassName);
		IElement FooterLink = Assert.Single(Footer.QuerySelectorAll("a"));
		Assert.Equal("noopener", FooterLink.GetAttribute("rel"));

		// 2. Top caption only (no footer)
		IElement TopOnly = Figures[1];
		Assert.Contains("article-image--caption-top", TopOnly.ClassList);
		Assert.Equal(2, TopOnly.Children.Length);
		Assert.Equal("figcaption", TopOnly.Children[0].TagName.ToLowerInvariant());
		Assert.Equal("article-image__media", TopOnly.Children[1].ClassName);
		Assert.Empty(TopOnly.QuerySelectorAll(".article-image__footer"));

		// 3. Credit only with CaptionPosition=top still renders credit at bottom in figcaption
		IElement CreditOnly = Figures[2];
		Assert.DoesNotContain("article-image--caption-top", CreditOnly.ClassList);
		Assert.Equal(2, CreditOnly.Children.Length);
		Assert.Equal("article-image__media", CreditOnly.Children[0].ClassName);
		Assert.Equal("figcaption", CreditOnly.Children[1].TagName.ToLowerInvariant());
		Assert.Equal("Credit stays at bottom.", Assert.Single(CreditOnly.QuerySelectorAll(".article-image__credit")).TextContent);

		// 4. Explicit CaptionPosition=bottom
		IElement ExplicitBottom = Figures[3];
		Assert.DoesNotContain("article-image--caption-top", ExplicitBottom.ClassList);
		Assert.Equal("article-image__media", ExplicitBottom.Children[0].ClassName);
		Assert.Equal("figcaption", ExplicitBottom.Children[1].TagName.ToLowerInvariant());
	}

	/// <summary>
	/// Verifies <see cref="ArticleSaveWarnings.Find"/> reports unknown properties, invalid enum values,
	/// and ineffective <c>Size</c> or <c>CaptionPosition</c> combinations while staying silent on valid directives.
	/// </summary>
	[Fact]
	public void SaveWarningsReportInvalidImageOptionsAndAcceptValidDirectives()
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineFactory(new TrustedLinkDomainList()).GetPipeline();

		const string Valid = "{{Image file:rabbit-logo |#| Layout=aside |#| Size=small |#| Frame=paper |#| Alt=Logo |#| Caption=Caption |#| CaptionPosition=top |#| Credit=Credit |#| Link=file}}";
		Assert.Empty(ArticleSaveWarnings.Find(Valid, Pipeline, TestInfoboxSchemas.Catalog, 1));

		string Invalid = string.Join("\n\n",
		[
			"{{Image file:rabbit-logo |#| Align=right |#| Layout=floating |#| Size=huge |#| Frame=gold |#| CaptionPosition=side |#| Link=popup}}",
			"{{Image file:rabbit-logo |#| Layout=full |#| Size=small |#| CaptionPosition=top}}"
		]);

		IReadOnlyList<string> Warnings = ArticleSaveWarnings.Find(Invalid, Pipeline, TestInfoboxSchemas.Catalog, 1);
		Assert.Contains(Warnings, w => w.Contains("Line 1:", StringComparison.Ordinal) && w.Contains("unknown property \"Align\"", StringComparison.Ordinal));
		Assert.Contains(Warnings, w => w.Contains("Line 1:", StringComparison.Ordinal) && w.Contains("Layout=\"floating\"", StringComparison.Ordinal));
		Assert.Contains(Warnings, w => w.Contains("Line 1:", StringComparison.Ordinal) && w.Contains("Size=\"huge\"", StringComparison.Ordinal));
		Assert.Contains(Warnings, w => w.Contains("Line 1:", StringComparison.Ordinal) && w.Contains("Frame=\"gold\"", StringComparison.Ordinal));
		Assert.Contains(Warnings, w => w.Contains("Line 1:", StringComparison.Ordinal) && w.Contains("CaptionPosition=\"side\"", StringComparison.Ordinal));
		Assert.Contains(Warnings, w => w.Contains("Line 1:", StringComparison.Ordinal) && w.Contains("Link=\"popup\"", StringComparison.Ordinal));
		Assert.Contains(Warnings, w => w.Contains("Line 3:", StringComparison.Ordinal) && w.Contains("Size=\"small\" has no effect with Layout=full", StringComparison.Ordinal));
		Assert.Contains(Warnings, w => w.Contains("Line 3:", StringComparison.Ordinal) && w.Contains("CaptionPosition=\"top\" has no effect without Caption", StringComparison.Ordinal));
	}

	/// <summary>
	/// Seeds an in-memory SQLite database with one sample file article (<c>rabbit-logo</c>, 180×268 SVG)
	/// and renders the supplied Markdown through the production <see cref="ArticleRenderer"/>.
	/// </summary>
	/// <param name="MarkdownText">The article Markdown to render.</param>
	/// <param name="TrustedDomains">Optional trusted-domain list for link-policy tests.</param>
	/// <returns>The rendered article HTML.</returns>
	private static async Task<string> RenderWithSampleFileAsync(string MarkdownText, TrustedLinkDomainList? TrustedDomains = null)
	{
		await using SqliteConnection Connection = new("Data Source=:memory:");
		await Connection.OpenAsync();
		await using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		await Context.Database.EnsureCreatedAsync();

		Guid CanonicalFileId = Guid.NewGuid();
		Guid UserId = Guid.NewGuid();

		Context.ArticleRevisions.Add(new ArticleRevision
		{
			CanonicalArticleId = Guid.NewGuid(),
			SiteId = 1,
			Culture = "en",
			Title = "Rabbit Logo",
			UrlSlug = "rabbit-logo",
			Type = ArticleType.File,
			CanonicalFileId = CanonicalFileId,
			Text = "Vector logo.",
			IsCurrent = true,
			CreatedByUserId = UserId,
			DateCreated = DateTimeOffset.UtcNow,
			RevisionReason = "Seed file"
		});

		Context.FileRevisions.Add(new FileRevision
		{
			CanonicalFileId = CanonicalFileId,
			Type = FileType.Image2D,
			Filename = "rabbit-logo.svg",
			MimeType = "image/svg+xml",
			FileSizeBytes = 1140,
			ImageWidthPixels = 180,
			ImageHeightPixels = 268,
			SourceAndRevisionReasonCulture = "en",
			RevisionReason = "Seed upload",
			CreatedByUserId = UserId,
			DateCreated = DateTimeOffset.UtcNow,
			IsCurrent = true
		});

		await Context.SaveChangesAsync();

		ArticleRevision Article = new()
		{
			CanonicalArticleId = Guid.NewGuid(),
			SiteId = 1,
			Culture = "en",
			Title = "Playboy",
			UrlSlug = "playboy",
			Type = ArticleType.Article,
			Text = MarkdownText,
			IsCurrent = true,
			CreatedByUserId = UserId,
			DateCreated = DateTimeOffset.UtcNow,
			RevisionReason = "Test"
		};

		ArticleRenderer Renderer = new(
			Context,
			new MarkdownPipelineFactory(TrustedDomains ?? new TrustedLinkDomainList()),
			Options.Create(new SiteConfiguration()),
			TestInfoboxSchemas.Catalog);

		RenderedArticle Rendered = await Renderer.RenderAsync(Article, 1, "en", "/playboy/edit", CancellationToken.None);
		return Rendered.Html;
	}
}
