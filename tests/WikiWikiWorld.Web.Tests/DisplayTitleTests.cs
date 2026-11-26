using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Web.Pages.Article;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Verifies display-title text breaks, multi-color/themed logo tokens, and file backlink resolution.</summary>
public sealed class DisplayTitleTests
{
	/// <summary>Checks visible slash breaks without introducing authored HTML or changing plain-text titles.</summary>
	/// <param name="DisplayTitle">The optional authored display title.</param>
	/// <param name="FallbackTitle">The canonical title used when the display title is absent.</param>
	/// <param name="ExpectedHtml">The safe heading fragment including optional and explicit breaks.</param>
	/// <param name="ExpectedPlainText">The unchanged title for metadata and inline references.</param>
	[Theory]
	[InlineData("SPRING/SUMMER 2014", "Canonical", "SPRING/<wbr>SUMMER 2014", "SPRING/SUMMER 2014")]
	[InlineData(null, "SPRING/SUMMER", "SPRING/<wbr>SUMMER", "SPRING/SUMMER")]
	[InlineData("USA\r\nSPRING/SUMMER", "Canonical", "USA<br />SPRING/<wbr>SUMMER", "USA SPRING/SUMMER")]
	[InlineData("<script>alert(1)</script>/A&B", "Canonical", "&lt;script&gt;alert(1)&lt;/<wbr>script&gt;/<wbr>A&amp;B", "<script>alert(1)</script>/A&B")]
	[InlineData("Before/[[SPRING/SUMMER|#| ]]After/2014", "Canonical", "Before/<wbr>SPRING/<wbr>SUMMERAfter/<wbr>2014", "Before/SPRING/SUMMERAfter/2014")]
	[InlineData("[[GQ|#|gq-logo|#|color]]\nIssue 01", "GQ - Issue 01", "GQ<br />Issue 01", "GQ Issue 01")]
	[InlineData("[[Mojo|#|mojo-light|~|mojo-dark]]\nIssue 10", "Mojo - Issue 10", "Mojo<br />Issue 10", "Mojo Issue 10")]
	[InlineData("[[Mojo|#|mojo-light, mojo-dark]]", "Mojo", "Mojo", "Mojo")]
	[InlineData("[[Mojo|#|mojo-light|#|mojo-dark]]", "Mojo", "Mojo", "Mojo")]
	public async Task RenderingPreservesTextAndEncoding(string? DisplayTitle, string FallbackTitle, string ExpectedHtml, string ExpectedPlainText)
	{
		await using SqliteConnection Connection = await OpenSqliteWithSchemaAsync();
		await using WikiWikiWorldDbContext Context = CreateContext(Connection);
		string Html = await DisplayTitleHelper.RenderHtmlAsync(DisplayTitle, FallbackTitle, Context, 1, "en", CancellationToken.None);

		Assert.Equal(ExpectedHtml, Html);
		Assert.Equal(ExpectedPlainText, DisplayTitleHelper.ToPlainText(DisplayTitle, FallbackTitle));
		HtmlParser Parser = new();
		Assert.Null(Parser.ParseDocument(Html).QuerySelector("script"));
	}

	/// <summary>
	/// Verifies masked, unmasked color, and light/dark dual logo tokens (with both <c>|~|</c> and <c>,</c> separators)
	/// against SQLite file revisions, as well as fallback behavior for missing or invalid options.
	/// </summary>
	[Fact]
	public async Task LogoTokensRenderMaskColorAndLightDarkVariants()
	{
		await using SqliteConnection Connection = await OpenSqliteWithSchemaAsync();
		await using WikiWikiWorldDbContext Context = CreateContext(Connection);
		Guid AuthorId = await SeedUserAsync(Context);

		string OutlanderUrl = await SeedFileArticleAsync(Context, AuthorId, "outlander-logo", "outlander.svg", 400, 100);
		string GqUrl = await SeedFileArticleAsync(Context, AuthorId, "gq-logo", "gq.svg", 320, 160);
		string MojoLightUrl = await SeedFileArticleAsync(Context, AuthorId, "mojo-light", "mojo-light.svg", 500, 120);
		string MojoDarkUrl = await SeedFileArticleAsync(Context, AuthorId, "mojo-dark", "mojo-dark.svg", 510, 125);

		HtmlParser Parser = new();

		// 1. Default single-slug token renders as CSS mask span.
		string MaskHtml = await DisplayTitleHelper.RenderHtmlAsync("[[Outlander|#|outlander-logo]]\nIssue 01", "Outlander - Issue 01", Context, 1, "en");
		IHtmlDocument MaskDoc = Parser.ParseDocument(MaskHtml);
		IElement? MaskGraphic = MaskDoc.QuerySelector("span.title-logo:not(.title-logo--themed) > span.title-logo__graphic");
		Assert.NotNull(MaskGraphic);
		Assert.Contains(OutlanderUrl, MaskGraphic.GetAttribute("style"), StringComparison.Ordinal);
		Assert.Contains("--logo-width: 400; --logo-height: 100;", MaskGraphic.GetAttribute("style"), StringComparison.Ordinal);
		Assert.Equal("Outlander ", MaskDoc.QuerySelector(".visually-hidden")?.TextContent);
		Assert.Null(MaskDoc.QuerySelector("br"));

		// 2. |#|color renders an unmasked <img> with intrinsic dimensions and empty alt inside aria-hidden.
		string ColorHtml = await DisplayTitleHelper.RenderHtmlAsync("[[GQ|#|file:gq-logo|#|color]]", "GQ", Context, 1, "en");
		IHtmlDocument ColorDoc = Parser.ParseDocument(ColorHtml);
		IElement? ColorImg = ColorDoc.QuerySelector("span.title-logo:not(.title-logo--themed) > img.title-logo__img");
		Assert.NotNull(ColorImg);
		Assert.Equal(GqUrl, ColorImg.GetAttribute("src"));
		Assert.Equal(string.Empty, ColorImg.GetAttribute("alt"));
		Assert.Equal("320", ColorImg.GetAttribute("width"));
		Assert.Equal("160", ColorImg.GetAttribute("height"));
		Assert.Equal("true", ColorImg.GetAttribute("aria-hidden"));
		Assert.Equal("GQ ", ColorDoc.QuerySelector(".visually-hidden")?.TextContent);

		// 3. Light/dark pair with |~| renders unmasked light and dark <img> variants by default.
		string TildePairHtml = await DisplayTitleHelper.RenderHtmlAsync("[[Mojo|#|mojo-light|~|mojo-dark]]", "Mojo", Context, 1, "en");
		IHtmlDocument TildeDoc = Parser.ParseDocument(TildePairHtml);
		IElement? LightImg = TildeDoc.QuerySelector("span.title-logo.title-logo--themed > img.title-logo__img.title-logo__light");
		IElement? DarkImg = TildeDoc.QuerySelector("span.title-logo.title-logo--themed > img.title-logo__img.title-logo__dark");
		Assert.NotNull(LightImg);
		Assert.NotNull(DarkImg);
		Assert.Equal(MojoLightUrl, LightImg.GetAttribute("src"));
		Assert.Equal(MojoDarkUrl, DarkImg.GetAttribute("src"));
		Assert.Equal("500", LightImg.GetAttribute("width"));
		Assert.Equal("510", DarkImg.GetAttribute("width"));

		// 4. Light/dark pair with comma separator renders identically.
		string CommaPairHtml = await DisplayTitleHelper.RenderHtmlAsync("[[Mojo|#|mojo-light, file:mojo-dark]]", "Mojo", Context, 1, "en");
		Assert.Equal(TildePairHtml, CommaPairHtml);

		// 5. Light/dark pair with explicit |#|mask renders masked spans.
		string MaskedPairHtml = await DisplayTitleHelper.RenderHtmlAsync("[[Mojo|#|mojo-light|~|mojo-dark|#|mask]]", "Mojo", Context, 1, "en");
		IHtmlDocument MaskedPairDoc = Parser.ParseDocument(MaskedPairHtml);
		Assert.NotNull(MaskedPairDoc.QuerySelector("span.title-logo.title-logo--themed > span.title-logo__graphic.title-logo__light"));
		Assert.NotNull(MaskedPairDoc.QuerySelector("span.title-logo.title-logo--themed > span.title-logo__graphic.title-logo__dark"));

		// 6. Missing dark file or misusing |#| for a second slug falls back cleanly to plain text.
		string MissingDarkHtml = await DisplayTitleHelper.RenderHtmlAsync("[[Mojo|#|mojo-light|~|mojo-missing]]", "Mojo", Context, 1, "en");
		Assert.Equal("Mojo", MissingDarkHtml);

		string WrongSeparatorHtml = await DisplayTitleHelper.RenderHtmlAsync("[[Mojo|#|mojo-light|#|mojo-dark]]", "Mojo", Context, 1, "en");
		Assert.Equal("Mojo", WrongSeparatorHtml);
	}

	/// <summary>
	/// Verifies that file backlinks (<c>/file:{slug}/links</c>) match single logos, <c>|#|color</c> logos,
	/// and both light and dark slugs separated by <c>|~|</c> or <c>,</c>, without substring false positives.
	/// </summary>
	[Fact]
	public async Task FileLinksPageFindsAllLogoTokenVariantsWithoutSubstringFalsePositives()
	{
		await using SqliteConnection Connection = await OpenSqliteWithSchemaAsync();
		SiteResolverService Resolver = CreateSiteResolver();
		SiteContextService SiteContext = new(Resolver, CreateHttpContextAccessor());
		await using WikiWikiWorldDbContext Context = CreateContext(Connection, SiteContext);
		Guid AuthorId = await SeedUserAsync(Context);

		await SeedFileArticleAsync(Context, AuthorId, "gq-logo", "gq.svg", 320, 160);
		await SeedFileArticleAsync(Context, AuthorId, "gq", "gq-other.svg", 100, 100);
		await SeedFileArticleAsync(Context, AuthorId, "mojo-light", "mojo-light.svg", 500, 120);
		await SeedFileArticleAsync(Context, AuthorId, "mojo-dark", "mojo-dark.svg", 500, 120);

		Context.ArticleRevisions.AddRange(
			MakeArticleRevision(AuthorId, "GQ", "gq-mag", "[[GQ|#|gq-logo|#|color]]"),
			MakeArticleRevision(AuthorId, "Mojo UK", "mojo-uk", "[[Mojo|#|mojo-light|~|mojo-dark]]"),
			MakeArticleRevision(AuthorId, "Mojo US", "mojo-us", "[[Mojo|#|file:mojo-light, file:mojo-dark]]"));
		await Context.SaveChangesAsync();

		Assert.Equal(["gq-mag"], await GetBacklinkSlugsAsync(Context, "file:gq-logo"));
		Assert.Empty(await GetBacklinkSlugsAsync(Context, "file:gq"));
		Assert.Equal(["mojo-uk", "mojo-us"], [.. (await GetBacklinkSlugsAsync(Context, "file:mojo-light")).OrderBy(S => S, StringComparer.Ordinal)]);
		Assert.Equal(["mojo-uk", "mojo-us"], [.. (await GetBacklinkSlugsAsync(Context, "file:mojo-dark")).OrderBy(S => S, StringComparer.Ordinal)]);
	}

	/// <summary>
	/// Opens an in-memory SQLite database initialized with <c>Create.sql</c>.
	/// </summary>
	/// <returns>An open <see cref="SqliteConnection"/> containing the fresh schema.</returns>
	private static async Task<SqliteConnection> OpenSqliteWithSchemaAsync()
	{
		SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
		await Connection.OpenAsync();
		string CreateSql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", "Create.sql"));
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = CreateSql;
		await Command.ExecuteNonQueryAsync();
		return Connection;
	}

	/// <summary>
	/// Creates a <see cref="WikiWikiWorldDbContext"/> bound to an open SQLite connection.
	/// </summary>
	/// <param name="Connection">The open SQLite connection.</param>
	/// <param name="SiteContext">Optional site context for query filters.</param>
	/// <returns>A configured <see cref="WikiWikiWorldDbContext"/>.</returns>
	private static WikiWikiWorldDbContext CreateContext(SqliteConnection Connection, ISiteContextService? SiteContext = null)
	{
		DbContextOptions<WikiWikiWorldDbContext> Options = new DbContextOptionsBuilder<WikiWikiWorldDbContext>()
			.UseSqlite(Connection)
			.Options;
		return SiteContext is null ? new WikiWikiWorldDbContext(Options) : new WikiWikiWorldDbContext(Options, SiteContext);
	}

	/// <summary>
	/// Seeds a contributor user row for foreign-key integrity.
	/// </summary>
	/// <param name="Context">The database context.</param>
	/// <returns>The created user ID.</returns>
	private static async Task<Guid> SeedUserAsync(WikiWikiWorldDbContext Context)
	{
		Guid UserId = Guid.NewGuid();
		Context.Users.Add(new User
		{
			Id = UserId,
			UserName = "Editor",
			NormalizedUserName = "EDITOR",
			DateCreated = DateTimeOffset.UtcNow
		});
		await Context.SaveChangesAsync();
		return UserId;
	}

	/// <summary>
	/// Seeds a current <see cref="FileRevision"/> and its associated <see cref="ArticleType.File"/> article revision.
	/// </summary>
	/// <param name="Context">The database context.</param>
	/// <param name="AuthorId">The author user ID.</param>
	/// <param name="UrlSlug">The bare file article slug.</param>
	/// <param name="Filename">The stored filename.</param>
	/// <param name="WidthPx">The intrinsic image width in pixels.</param>
	/// <param name="HeightPx">The intrinsic image height in pixels.</param>
	/// <returns>The expected public image URL built by <see cref="ImageUrlHelper.BuildImageUrl"/>.</returns>
	private static async Task<string> SeedFileArticleAsync(
		WikiWikiWorldDbContext Context,
		Guid AuthorId,
		string UrlSlug,
		string Filename,
		int WidthPx,
		int HeightPx)
	{
		Guid CanonicalFileId = Guid.NewGuid();
		FileRevision FileRev = new()
		{
			CanonicalFileId = CanonicalFileId,
			IsCurrent = true,
			Type = FileType.Image2D,
			Filename = Filename,
			MimeType = "image/svg+xml",
			FileSizeBytes = 2048,
			ImageWidthPixels = WidthPx,
			ImageHeightPixels = HeightPx,
			RevisionReason = "Seed logo",
			SourceAndRevisionReasonCulture = "en",
			CreatedByUserId = AuthorId,
			DateCreated = DateTimeOffset.UtcNow
		};
		Context.FileRevisions.Add(FileRev);
		Context.ArticleRevisions.Add(new ArticleRevision
		{
			CanonicalArticleId = Guid.NewGuid(),
			CanonicalFileId = CanonicalFileId,
			SiteId = 1,
			Culture = "en",
			Title = $"File: {UrlSlug}",
			UrlSlug = UrlSlug,
			IsCurrent = true,
			Type = ArticleType.File,
			Text = "Logo file.",
			RevisionReason = "Seed file article",
			CreatedByUserId = AuthorId,
			DateCreated = DateTimeOffset.UtcNow
		});
		await Context.SaveChangesAsync();
		return ImageUrlHelper.BuildImageUrl(1, CanonicalFileId, FileRev.Id, Filename);
	}

	/// <summary>
	/// Builds an ordinary current <see cref="ArticleRevision"/> with the specified display title.
	/// </summary>
	/// <param name="AuthorId">The author user ID.</param>
	/// <param name="Title">The canonical article title.</param>
	/// <param name="UrlSlug">The article URL slug.</param>
	/// <param name="DisplayTitle">The display title containing logo tokens.</param>
	/// <returns>The constructed <see cref="ArticleRevision"/>.</returns>
	private static ArticleRevision MakeArticleRevision(Guid AuthorId, string Title, string UrlSlug, string DisplayTitle) => new()
	{
		CanonicalArticleId = Guid.NewGuid(),
		SiteId = 1,
		Culture = "en",
		Title = Title,
		DisplayTitle = DisplayTitle,
		UrlSlug = UrlSlug,
		IsCurrent = true,
		Type = ArticleType.Article,
		Text = "Article body without direct file links.",
		RevisionReason = "Seed article",
		CreatedByUserId = AuthorId,
		DateCreated = DateTimeOffset.UtcNow
	};

	/// <summary>
	/// Executes <see cref="LinksModel.OnGetAsync"/> for a canonical file slug and returns the backlink slugs.
	/// </summary>
	/// <param name="Context">The database context.</param>
	/// <param name="CanonicalFileSlug">The canonical <c>file:...</c> slug.</param>
	/// <returns>The list of matching backlink article slugs.</returns>
	private static async Task<IReadOnlyList<string>> GetBacklinkSlugsAsync(WikiWikiWorldDbContext Context, string CanonicalFileSlug)
	{
		IHttpContextAccessor Accessor = CreateHttpContextAccessor();
		SiteResolverService Resolver = CreateSiteResolver(Accessor);
		PageContext RazorPageContext = new(new ActionContext(Accessor.HttpContext!, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary()));

		LinksModel Page = new(Context, Resolver)
		{
			UrlSlug = CanonicalFileSlug,
			PageContext = RazorPageContext
		};
		Page.OnPageHandlerExecuting(new PageHandlerExecutingContext(
			RazorPageContext,
			[],
			null,
			new Dictionary<string, object?>(StringComparer.Ordinal),
			Page));

		IActionResult Result = await Page.OnGetAsync(CancellationToken.None);
		Assert.IsType<PageResult>(Result);
		return [.. Page.Backlinks.Select(B => B.UrlSlug)];
	}

	/// <summary>
	/// Creates an <see cref="IHttpContextAccessor"/> targeting the English Magazedia host.
	/// </summary>
	/// <returns>The configured <see cref="IHttpContextAccessor"/>.</returns>
	private static IHttpContextAccessor CreateHttpContextAccessor()
	{
		DefaultHttpContext Http = new();
		Http.Request.Scheme = "https";
		Http.Request.Host = new HostString("en.magazedia.com.localhost");
		return new HttpContextAccessor { HttpContext = Http };
	}

	/// <summary>
	/// Creates a <see cref="SiteResolverService"/> configured for site 1 (<c>en.magazedia.com.localhost</c>).
	/// </summary>
	/// <param name="Accessor">Optional HTTP context accessor to reuse.</param>
	/// <returns>The configured <see cref="SiteResolverService"/>.</returns>
	private static SiteResolverService CreateSiteResolver(IHttpContextAccessor? Accessor = null) =>
		new(
			Options.Create(new SiteConfiguration
			{
				Sites =
				[
					new SiteInfo
					{
						SiteId = 1,
						SiteName = "Magazedia",
						DefaultCulture = "en",
						Domains = ["magazedia.com.localhost", "en.magazedia.com.localhost"]
					}
				]
			}),
			Accessor ?? CreateHttpContextAccessor());
}
