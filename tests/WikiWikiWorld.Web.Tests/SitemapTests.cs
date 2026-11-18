using System.Data.Common;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Web.Pages.Info;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Exercises scalable HTML/XML sitemap generation, index usage, routing, and schema upgrades against SQLite.
/// </summary>
public sealed class SitemapTests
{
	private static readonly XNamespace SitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";

	/// <summary>
	/// Verifies fresh schema, upgrade script, precheck/verify SQL, and SQLite index plans for sitemap queries.
	/// </summary>
	/// <param name="SchemaSource">The schema setup path to exercise.</param>
	[Theory]
	[InlineData("Create")]
	[InlineData("Upgrade")]
	public async Task SchemaAndCoveringIndexesAreUsedBySqlite(string SchemaSource)
	{
		await using SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
		await Connection.OpenAsync();

		string CreateSql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", "Create.sql"));
		if (SchemaSource == "Create")
		{
			Execute(Connection, CreateSql);
		}
		else
		{
			Execute(Connection, CreateSql);
			Execute(Connection, "DROP INDEX IX_ArticleRevisions_Sitemap_Html; DROP INDEX IX_ArticleRevisions_Sitemap_Xml;");
			Assert.Equal(1L, ExecuteScalar(Connection, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", "CheckSitemapIndexesBefore.sql"))));
			Execute(Connection, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", "AddSitemapIndexes.sql")));
		}

		Assert.Equal(1L, ExecuteScalar(Connection, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", "VerifySitemapIndexes.sql"))));

		string HtmlPlan = ExplainQueryPlan(Connection,
			"SELECT Id, Title, DisplayTitle, UrlSlug FROM ArticleRevisions " +
			"WHERE SiteId = 1 AND Culture = 'en' AND IsCurrent AND DateDeleted IS NULL AND Type = 0 " +
			"AND Title COLLATE NOCASE >= 'a' AND Title COLLATE NOCASE < 'b' " +
			"ORDER BY Title COLLATE NOCASE, Id LIMIT 200 OFFSET 0;");
		Assert.Contains("IX_ArticleRevisions_Sitemap_Html", HtmlPlan);

		string XmlPlan = ExplainQueryPlan(Connection,
			"SELECT UrlSlug, DateCreated FROM ArticleRevisions " +
			"WHERE SiteId = 1 AND Culture = 'en' AND IsCurrent AND DateDeleted IS NULL AND Type = 0 " +
			"AND Id >= 1 AND Id <= 50000 ORDER BY Id;");
		Assert.Contains("IX_ArticleRevisions_Sitemap_Xml", XmlPlan);

		await using WikiWikiWorldDbContext Context = CreateContext(Connection);
		IQueryable EfBucketQuery = SitemapService.ApplyBucketFilter(
				Context.ArticleRevisions
					.AsNoTracking()
					.Where(r => r.SiteId == 1 && r.Culture == "en" && r.IsCurrent && r.Type == ArticleType.Article),
				"a")
			.OrderBy(r => EF.Functions.Collate(r.Title, "NOCASE"))
			.ThenBy(r => r.Id)
			.Skip(0)
			.Take(200)
			.Select(r => new { r.UrlSlug, r.Title, r.DisplayTitle, r.Type });

		string EfBucketPlan = await ExplainEfQueryPlanAsync(Connection, EfBucketQuery);
		Assert.True(EfBucketPlan.Contains("IX_ArticleRevisions_Sitemap_Html", StringComparison.Ordinal),
			$"Unexpected plan: {EfBucketPlan} | SQL: {EfBucketQuery.CreateDbCommand().CommandText}");
		Assert.DoesNotContain("TEMP B-TREE", EfBucketPlan, StringComparison.OrdinalIgnoreCase);

		IQueryable EfUnfilteredQuery = Context.ArticleRevisions
			.AsNoTracking()
			.Where(r => r.SiteId == 1 && r.Culture == "en" && r.IsCurrent && r.Type == ArticleType.Article)
			.OrderBy(r => EF.Functions.Collate(r.Title, "NOCASE"))
			.ThenBy(r => r.Id)
			.Skip(0)
			.Take(200)
			.Select(r => new { r.UrlSlug, r.Title, r.DisplayTitle, r.Type });

		string EfUnfilteredPlan = await ExplainEfQueryPlanAsync(Connection, EfUnfilteredQuery);
		Assert.True(EfUnfilteredPlan.Contains("IX_ArticleRevisions_Sitemap_Html", StringComparison.Ordinal),
			$"Unexpected unfiltered plan: {EfUnfilteredPlan} | SQL: {EfUnfilteredQuery.CreateDbCommand().CommandText}");
		Assert.DoesNotContain("TEMP B-TREE", EfUnfilteredPlan, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Verifies HTML hub summaries, bucket filtering, scoping, display-title rendering, and pagination.
	/// </summary>
	[Fact]
	public async Task HtmlSitemapHubAndSectionPaginationFilterAndSortProperly()
	{
		await using SqliteConnection Connection = new("Data Source=:memory:");
		await Connection.OpenAsync();
		Execute(Connection, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", "Create.sql")));

		await using WikiWikiWorldDbContext Context = CreateContext(Connection);
		Guid AuthorId = await SeedUserAsync(Context);

		// Seed 205 articles starting with 'A'/'a' to force 2 HTML pages (page size = 200),
		// plus digit, non-ASCII/symbol, file, category, info, and excluded rows.
		for (int i = 1; i <= 205; i++)
		{
			string Prefix = i % 2 == 0 ? "a" : "A";
			Context.ArticleRevisions.Add(MakeRevision(AuthorId, $"{Prefix}Magazine {i:D3}", $"a-magazine-{i:D3}", ArticleType.Article));
		}

		Context.ArticleRevisions.AddRange(
			MakeRevision(AuthorId, "2000 AD", "2000-ad", ArticleType.Article),
			MakeRevision(AuthorId, "Écho", "echo", ArticleType.Article),
			MakeRevision(AuthorId, "雑誌", "zasshi", ArticleType.Article),
			MakeRevision(AuthorId, "Science Magazines", "science-magazines", ArticleType.Category),
			MakeRevision(AuthorId, "Cover Scan", "cover-scan", ArticleType.File, DisplayTitle: "[[Cover Logo|#|cover-logo]]\nScan"),
			MakeRevision(AuthorId, "Editorial Policy", "editorial-policy", ArticleType.Info),
			MakeRevision(AuthorId, "1984", "1984", ArticleType.Redirect),
			MakeRevision(AuthorId, "@Editor", "@editor", ArticleType.User),
			MakeRevision(AuthorId, "Old Title", "old-title", ArticleType.Article, IsCurrent: false),
			MakeRevision(AuthorId, "Deleted Title", "deleted-title", ArticleType.Article, DateDeleted: DateTimeOffset.UtcNow),
			MakeRevision(AuthorId, "Japanese Only", "japanese-only", ArticleType.Article, Culture: "ja"),
			MakeRevision(AuthorId, "Other Site", "other-site", ArticleType.Article, SiteId: 2));
		await Context.SaveChangesAsync();

		using MemoryCache Cache = new(new MemoryCacheOptions());
		SitemapService Service = CreateService(Context, Cache, "https", "en.magazedia.com");

		HtmlSitemapHub Hub = await Service.GetHtmlHubAsync(1, "en", "https", "en.magazedia.com", null);
		Assert.Equal(212, Hub.TotalContentCount); // 208 articles + 1 category + 1 file + 1 info + 1 redirect
		Assert.Contains(Hub.CultureLinks, C => C.CultureCode == "ja" && C.Url == "https://ja.magazedia.com/info:sitemap");

		SitemapHubSection RedirectsHub = Assert.Single(Hub.Sections, S => S.Section.Slug == "redirects");
		Assert.Equal(1, RedirectsHub.TotalCount);
		Assert.True(RedirectsHub.Buckets.First(B => B.Slug == "0-9").HasEntries);

		SitemapHubSection ArticlesHub = Assert.Single(Hub.Sections, S => S.Section.Slug == "articles");
		Assert.Equal(208, ArticlesHub.TotalCount);
		Assert.True(ArticlesHub.Buckets.First(B => B.Slug == "a").HasEntries);
		Assert.False(ArticlesHub.Buckets.First(B => B.Slug == "b").HasEntries);
		Assert.True(ArticlesHub.Buckets.First(B => B.Slug == "0-9").HasEntries);
		Assert.True(ArticlesHub.Buckets.First(B => B.Slug == "other").HasEntries);

		// Unfiltered page 1 and page 2
		HtmlSitemapSectionPage? Page1 = await Service.GetHtmlSectionPageAsync(1, "en", "articles", null, 1);
		Assert.NotNull(Page1);
		Assert.Equal(2, Page1.TotalPages);
		Assert.Equal(200, Page1.Entries.Count);
		Assert.Equal("2000 AD", Page1.Entries[0].Title); // '0-9' sorts before 'a' in NOCASE

		HtmlSitemapSectionPage? Page2 = await Service.GetHtmlSectionPageAsync(1, "en", "articles", null, 2);
		Assert.NotNull(Page2);
		Assert.Equal(8, Page2.Entries.Count);
		Assert.Contains(Page2.Entries, E => E.Title == "Écho");
		Assert.Contains(Page2.Entries, E => E.Title == "雑誌");

		// Bucket 'a' has 205 items -> 2 pages
		HtmlSitemapSectionPage? BucketPage2 = await Service.GetHtmlSectionPageAsync(1, "en", "articles", "a", 2);
		Assert.NotNull(BucketPage2);
		Assert.Equal(205, BucketPage2.TotalItems);
		Assert.Equal(5, BucketPage2.Entries.Count);
		Assert.Equal("a", BucketPage2.ActiveBucket?.Slug);

		// Bucket 'other' has Écho and 雑誌
		HtmlSitemapSectionPage? OtherBucket = await Service.GetHtmlSectionPageAsync(1, "en", "articles", "other", 1);
		Assert.NotNull(OtherBucket);
		Assert.Equal(2, OtherBucket.TotalItems);

		// File entry uses DisplayTitleHelper.ToPlainText and /file: canonical path
		HtmlSitemapSectionPage? FilesPage = await Service.GetHtmlSectionPageAsync(1, "en", "files", null, 1);
		Assert.NotNull(FilesPage);
		HtmlSitemapEntry FileEntry = Assert.Single(FilesPage.Entries);
		Assert.Equal("Cover Logo Scan", FileEntry.Title);
		Assert.Equal("/file:cover-scan", FileEntry.Path);

		// Redirects section appears in HTML sitemap
		HtmlSitemapSectionPage? RedirectsPage = await Service.GetHtmlSectionPageAsync(1, "en", "redirects", null, 1);
		Assert.NotNull(RedirectsPage);
		HtmlSitemapEntry RedirectEntry = Assert.Single(RedirectsPage.Entries);
		Assert.Equal("1984", RedirectEntry.Title);
		Assert.Equal("/1984", RedirectEntry.Path);

		// Invalid or out-of-range inputs return null
		Assert.Null(await Service.GetHtmlSectionPageAsync(1, "en", "articles", null, 3));
		Assert.Null(await Service.GetHtmlSectionPageAsync(1, "en", "unknown", null, 1));
		Assert.Null(await Service.GetHtmlSectionPageAsync(1, "en", "articles", "bad", 1));
		Assert.Null(await Service.GetHtmlSectionPageAsync(1, "en", "articles", null, 0));
	}

	/// <summary>
	/// Verifies XML sitemap switches seamlessly from a single urlset to a chunked sitemapindex above the chunk limit.
	/// </summary>
	[Fact]
	public async Task XmlSitemapSwitchesToChunkedSitemapIndexBeyondLimit()
	{
		await using SqliteConnection Connection = new("Data Source=:memory:");
		await Connection.OpenAsync();
		Execute(Connection, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", "Create.sql")));

		await using WikiWikiWorldDbContext Context = CreateContext(Connection);
		Guid AuthorId = await SeedUserAsync(Context);

		// 7 articles + 1 category + 1 excluded User page
		for (int i = 1; i <= 7; i++)
		{
			Context.ArticleRevisions.Add(MakeRevision(AuthorId, $"Title {i}", $"title-{i}", ArticleType.Article));
		}

		Context.ArticleRevisions.Add(MakeRevision(AuthorId, "Cat 1", "cat-1", ArticleType.Category));
		Context.ArticleRevisions.Add(MakeRevision(AuthorId, "Redirect 1", "redirect-1", ArticleType.Redirect));
		Context.ArticleRevisions.Add(MakeRevision(AuthorId, "@User", "@user", ArticleType.User));
		await Context.SaveChangesAsync();

		// 1) Small-site single <urlset> mode (limit 100 >= 7 static + 8 content)
		using MemoryCache SingleCache = new(new MemoryCacheOptions());
		SitemapService SingleService = CreateService(Context, SingleCache, "https", "en.magazedia.com", maxXmlUrlsPerFile: 100);
		XDocument SingleDoc = XDocument.Parse(await SingleService.GenerateSitemapAsync());
		Assert.Equal("urlset", SingleDoc.Root?.Name.LocalName);
		List<string> SingleLocs = [.. SingleDoc.Descendants(SitemapNs + "loc").Select(E => E.Value)];
		Assert.Contains("https://en.magazedia.com/info:sitemap", SingleLocs);
		Assert.Contains("https://en.magazedia.com/title-7", SingleLocs);
		Assert.DoesNotContain("https://en.magazedia.com/redirect-1", SingleLocs);
		Assert.DoesNotContain("https://en.magazedia.com/@user", SingleLocs);

		// 2) Chunked <sitemapindex> mode (limit 3)
		using MemoryCache ChunkCache = new(new MemoryCacheOptions());
		SitemapService ChunkService = CreateService(Context, ChunkCache, "https", "en.magazedia.com", maxXmlUrlsPerFile: 3);
		XDocument IndexDoc = XDocument.Parse(await ChunkService.GenerateSitemapAsync());
		Assert.Equal("sitemapindex", IndexDoc.Root?.Name.LocalName);
		List<string> IndexLocs = [.. IndexDoc.Descendants(SitemapNs + "loc").Select(E => E.Value)];
		Assert.Equal(
			[
				"https://en.magazedia.com/sitemaps/static.xml",
				"https://en.magazedia.com/sitemaps/articles-1.xml",
				"https://en.magazedia.com/sitemaps/articles-2.xml",
				"https://en.magazedia.com/sitemaps/articles-3.xml",
				"https://en.magazedia.com/sitemaps/categories-1.xml"
			],
			IndexLocs);

		// Verify each chunk returns exact non-overlapping articles
		List<string> CollectedArticleLocs = [];
		for (int Chunk = 1; Chunk <= 3; Chunk++)
		{
			string? ChunkXml = await ChunkService.GenerateSectionSitemapAsync("articles", Chunk);
			Assert.NotNull(ChunkXml);
			XDocument ChunkDoc = XDocument.Parse(ChunkXml);
			List<string> ChunkLocs = [.. ChunkDoc.Descendants(SitemapNs + "loc").Select(E => E.Value)];
			Assert.Equal(Chunk < 3 ? 3 : 1, ChunkLocs.Count);
			CollectedArticleLocs.AddRange(ChunkLocs);
		}

		Assert.Equal(7, CollectedArticleLocs.Distinct().Count());
		Assert.Null(await ChunkService.GenerateSectionSitemapAsync("articles", 4));
		Assert.Null(await ChunkService.GenerateSectionSitemapAsync("redirects", 1));
		Assert.Null(await ChunkService.GenerateSectionSitemapAsync("unknown", 1));
		Assert.NotNull(await ChunkService.GenerateStaticSitemapAsync());
	}

	/// <summary>
	/// Verifies culture-selector root domains return minimal root XML and reject chunked endpoints.
	/// </summary>
	[Fact]
	public async Task RootCultureSelectorDomainReturnsMinimalXmlOnly()
	{
		await using SqliteConnection Connection = new("Data Source=:memory:");
		await Connection.OpenAsync();
		Execute(Connection, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", "Create.sql")));

		await using WikiWikiWorldDbContext Context = CreateContext(Connection);
		using MemoryCache Cache = new(new MemoryCacheOptions());
		SitemapService Service = CreateService(Context, Cache, "https", "magazedia.com");

		XDocument RootDoc = XDocument.Parse(await Service.GenerateSitemapAsync());
		Assert.Equal("urlset", RootDoc.Root?.Name.LocalName);
		Assert.Equal("https://magazedia.com/", Assert.Single(RootDoc.Descendants(SitemapNs + "loc")).Value);
		Assert.Null(await Service.GenerateStaticSitemapAsync());
		Assert.Null(await Service.GenerateSectionSitemapAsync("articles", 1));
	}

	/// <summary>
	/// Verifies reserved slug protection and canonical lowercase/page-1 redirects on <see cref="SitemapSectionModel"/>.
	/// </summary>
	[Fact]
	public async Task RoutesProtectReservedSlugsAndCanonicalizeSectionUrls()
	{
		Assert.True(ArticleUrlHelper.IsRestrictedSlug("info:sitemap"));
		Assert.True(ArticleUrlHelper.IsRestrictedSlug("info:sitemap/articles"));
		Assert.True(ArticleUrlHelper.IsRestrictedSlug("sitemaps/articles-1.xml"));
		Assert.True(ArticleUrlHelper.IsRestrictedSlug("sitemap.xml"));

		await using SqliteConnection Connection = new("Data Source=:memory:");
		await Connection.OpenAsync();
		Execute(Connection, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SqliteScripts", "Create.sql")));

		await using WikiWikiWorldDbContext Context = CreateContext(Connection);
		using MemoryCache Cache = new(new MemoryCacheOptions());
		SitemapService Service = CreateService(Context, Cache, "https", "en.magazedia.com");

		SiteResolverService Resolver = CreateSiteResolver("https", "en.magazedia.com");
		SitemapSectionModel Model = new(Service, Resolver)
		{
			PageContext = CreatePageContext("https", "en.magazedia.com", "/info:sitemap/Articles/A/page/1")
		};

		IActionResult RedirectResult = await Model.OnGetAsync("Articles", "A", 1);
		RedirectResult TypedRedirect = Assert.IsType<RedirectResult>(RedirectResult);
		Assert.True(TypedRedirect.Permanent);
		Assert.Equal("/info:sitemap/articles/a", TypedRedirect.Url);

		Model.PageContext = CreatePageContext("https", "en.magazedia.com", "/info:sitemap/unknown");
		Assert.IsType<NotFoundResult>(await Model.OnGetAsync("unknown"));
	}

	/// <summary>
	/// Creates a <see cref="WikiWikiWorldDbContext"/> bound to the supplied open SQLite connection.
	/// </summary>
	/// <param name="Connection">The open SQLite connection.</param>
	/// <returns>The configured database context.</returns>
	private static WikiWikiWorldDbContext CreateContext(SqliteConnection Connection)
	{
		DbContextOptions<WikiWikiWorldDbContext> Options = new DbContextOptionsBuilder<WikiWikiWorldDbContext>()
			.UseSqlite(Connection)
			.Options;
		return new WikiWikiWorldDbContext(Options);
	}

	/// <summary>
	/// Creates a <see cref="SitemapService"/> configured for a specific host and chunk limit.
	/// </summary>
	/// <param name="Context">The database context.</param>
	/// <param name="Cache">The memory cache.</param>
	/// <param name="Scheme">The request scheme.</param>
	/// <param name="Host">The request host.</param>
	/// <param name="maxXmlUrlsPerFile">The maximum URLs per XML chunk.</param>
	/// <returns>The initialized sitemap service.</returns>
	private static SitemapService CreateService(
		WikiWikiWorldDbContext Context,
		IMemoryCache Cache,
		string Scheme,
		string Host,
		int maxXmlUrlsPerFile = SitemapService.DefaultMaxXmlUrlsPerFile)
	{
		IOptions<SiteConfiguration> SiteOptions = CreateSiteOptions();
		IHttpContextAccessor Accessor = CreateAccessor(Scheme, Host);
		SiteResolverService Resolver = new(SiteOptions, Accessor);
		return new SitemapService(Context, Resolver, Cache, Accessor, SiteOptions, maxXmlUrlsPerFile);
	}

	/// <summary>
	/// Creates a <see cref="SiteResolverService"/> for the specified request scheme and host.
	/// </summary>
	/// <param name="Scheme">The request scheme.</param>
	/// <param name="Host">The request host.</param>
	/// <returns>The site resolver service.</returns>
	private static SiteResolverService CreateSiteResolver(string Scheme, string Host) =>
		new(CreateSiteOptions(), CreateAccessor(Scheme, Host));

	/// <summary>
	/// Builds test site configuration options covering root culture-selector and subdomain hosts.
	/// </summary>
	/// <returns>Configured <see cref="IOptions{SiteConfiguration}"/>.</returns>
	private static IOptions<SiteConfiguration> CreateSiteOptions() => Options.Create(new SiteConfiguration
	{
		Sites =
		[
			new()
			{
				SiteId = 1,
				SiteName = "Magazedia",
				DefaultCulture = "en",
				RootDomainIsCultureSelectorOnly = true,
				Domains = ["magazedia.com", "en.magazedia.com", "ja.magazedia.com"]
			},
			new()
			{
				SiteId = 2,
				SiteName = "Cinepedia",
				DefaultCulture = "en",
				Domains = ["cinepedia.wiki", "en.cinepedia.wiki"]
			}
		]
	});

	/// <summary>
	/// Builds an <see cref="IHttpContextAccessor"/> populated with the given scheme and host.
	/// </summary>
	/// <param name="Scheme">The request scheme.</param>
	/// <param name="Host">The request host.</param>
	/// <returns>The populated HTTP context accessor.</returns>
	private static IHttpContextAccessor CreateAccessor(string Scheme, string Host)
	{
		DefaultHttpContext Http = new();
		Http.Request.Scheme = Scheme;
		Http.Request.Host = new HostString(Host);
		return new HttpContextAccessor { HttpContext = Http };
	}

	/// <summary>
	/// Builds a Razor <see cref="PageContext"/> for the specified request URL path.
	/// </summary>
	/// <param name="Scheme">The request scheme.</param>
	/// <param name="Host">The request host.</param>
	/// <param name="Path">The request path.</param>
	/// <returns>The initialized <see cref="PageContext"/>.</returns>
	private static PageContext CreatePageContext(string Scheme, string Host, string Path)
	{
		DefaultHttpContext Http = new();
		Http.Request.Scheme = Scheme;
		Http.Request.Host = new HostString(Host);
		Http.Request.Path = Path;
		return new PageContext(new ActionContext(Http, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary()));
	}

	/// <summary>
	/// Seeds and persists a contributor account for foreign-key integrity in test fixtures.
	/// </summary>
	/// <param name="Context">The database context.</param>
	/// <returns>The seeded user ID.</returns>
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
	/// Creates a valid <see cref="ArticleRevision"/> entity for sitemap test fixtures.
	/// </summary>
	/// <param name="AuthorId">The author user ID.</param>
	/// <param name="Title">The canonical article title.</param>
	/// <param name="UrlSlug">The article URL slug.</param>
	/// <param name="Type">The article type.</param>
	/// <param name="SiteId">The site ID.</param>
	/// <param name="Culture">The culture code.</param>
	/// <param name="IsCurrent">Whether the revision is current.</param>
	/// <param name="DisplayTitle">The optional display title.</param>
	/// <param name="DateDeleted">The optional soft-delete timestamp.</param>
	/// <returns>The constructed <see cref="ArticleRevision"/>.</returns>
	private static ArticleRevision MakeRevision(
		Guid AuthorId,
		string Title,
		string UrlSlug,
		ArticleType Type,
		int SiteId = 1,
		string Culture = "en",
		bool IsCurrent = true,
		string? DisplayTitle = null,
		DateTimeOffset? DateDeleted = null) => new()
	{
		CanonicalArticleId = Guid.NewGuid(),
		SiteId = SiteId,
		Culture = Culture,
		Title = Title,
		DisplayTitle = DisplayTitle,
		UrlSlug = UrlSlug,
		IsCurrent = IsCurrent,
		Type = Type,
		Text = "Body text that should never be loaded by sitemap queries.",
		RevisionReason = "Test",
		CreatedByUserId = AuthorId,
		DateCreated = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero),
		DateDeleted = DateDeleted
	};

	/// <summary>
	/// Executes non-query SQL against the test SQLite connection.
	/// </summary>
	/// <param name="Connection">The open SQLite connection.</param>
	/// <param name="Sql">The SQL statement or script to run.</param>
	private static void Execute(SqliteConnection Connection, string Sql)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = Sql;
		Command.ExecuteNonQuery();
	}

	/// <summary>
	/// Executes a scalar SQL query and converts the result to a 64-bit integer.
	/// </summary>
	/// <param name="Connection">The open SQLite connection.</param>
	/// <param name="Sql">The SQL query to execute.</param>
	/// <returns>The integer scalar result.</returns>
	private static long ExecuteScalar(SqliteConnection Connection, string Sql)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = Sql;
		return Convert.ToInt64(Command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Returns the concatenated SQLite <c>EXPLAIN QUERY PLAN</c> detail lines for an EF Core query.
	/// </summary>
	/// <param name="Connection">The open SQLite connection.</param>
	/// <param name="Query">The EF Core query.</param>
	/// <returns>The joined query plan detail strings.</returns>
	private static async Task<string> ExplainEfQueryPlanAsync(SqliteConnection Connection, IQueryable Query)
	{
		await using DbCommand EfCommand = Query.CreateDbCommand();
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = $"EXPLAIN QUERY PLAN {EfCommand.CommandText}";
		foreach (DbParameter Parameter in EfCommand.Parameters)
		{
			Command.Parameters.AddWithValue(Parameter.ParameterName, Parameter.Value ?? DBNull.Value);
		}

		using SqliteDataReader Reader = await Command.ExecuteReaderAsync();
		List<string> Details = [];
		while (await Reader.ReadAsync())
		{
			Details.Add(Reader.GetString(3));
		}

		return string.Join(" | ", Details);
	}

	/// <summary>
	/// Returns the concatenated SQLite <c>EXPLAIN QUERY PLAN</c> detail lines for a query.
	/// </summary>
	/// <param name="Connection">The open SQLite connection.</param>
	/// <param name="Sql">The query to explain.</param>
	/// <returns>The joined query plan detail strings.</returns>
	private static string ExplainQueryPlan(SqliteConnection Connection, string Sql)
	{
		using SqliteCommand Command = Connection.CreateCommand();
		Command.CommandText = $"EXPLAIN QUERY PLAN {Sql}";
		using SqliteDataReader Reader = Command.ExecuteReader();
		List<string> Details = [];
		while (Reader.Read())
		{
			Details.Add(Reader.GetString(3));
		}

		return string.Join(" | ", Details);
	}
}
