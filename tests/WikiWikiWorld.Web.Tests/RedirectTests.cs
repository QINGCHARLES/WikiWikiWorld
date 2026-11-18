using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.MarkdigExtensions;
using WikiWikiWorld.Web.Pages;
using WikiWikiWorld.Web.Pages.Article;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies <c>{{Redirect ...}}</c> parsing, HTTP redirect + TempData flash notice behavior,
/// save validation, and search integration for <see cref="ArticleType.Redirect"/>.
/// </summary>
public sealed class RedirectTests : IDisposable
{
	private readonly SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
	private readonly WikiWikiWorldDbContext Context;
	private readonly UserManager<User> Users;
	private readonly string StorageRoot = Path.Combine(Path.GetTempPath(), $"WikiWikiWorld.RedirectTests-{Guid.NewGuid():N}");
	private readonly User EditorUser = new() { Id = Guid.NewGuid(), UserName = "Editor", NormalizedUserName = "EDITOR" };
	private readonly IOptions<SiteConfiguration> Configuration = Options.Create(new SiteConfiguration
	{
		Sites = [new() { SiteId = 1, Domains = ["example.test"], DefaultCulture = "en" }]
	});

	/// <summary>
	/// Initializes an in-memory SQLite database and Identity user store for redirect tests.
	/// </summary>
	public RedirectTests()
	{
		Connection.Open();
		Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		Context.Database.EnsureCreated();
		Context.Users.Add(EditorUser);
		Context.SaveChanges();
		Users = new(
			new UserStore<User, Role, WikiWikiWorldDbContext, Guid>(Context),
			Options.Create(new IdentityOptions()),
			new PasswordHasher<User>(),
			[],
			[],
			new UpperInvariantLookupNormalizer(),
			new IdentityErrorDescriber(),
			null!,
			NullLogger<UserManager<User>>.Instance);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Users.Dispose();
		Context.Dispose();
		Connection.Dispose();
		if (Directory.Exists(StorageRoot))
		{
			Directory.Delete(StorageRoot, recursive: true);
		}
	}

	/// <summary>
	/// Verifies <see cref="RedirectExtension.TryExtractTarget"/> accepts valid directives outside code blocks and rejects unsafe or malformed targets.
	/// </summary>
	[Fact]
	public void TryExtractTargetParsesValidTargetsAndIgnoresCodeAndUnsafeInputs()
	{
		Assert.True(RedirectExtension.TryExtractTarget("{{Redirect 1994}}", out string Target1, out string? Frag1));
		Assert.Equal("1994", Target1);
		Assert.Null(Frag1);

		Assert.True(RedirectExtension.TryExtractTarget("{{redirect /1994#publishing-history}}", out string Target2, out string? Frag2));
		Assert.Equal("1994", Target2);
		Assert.Equal("publishing-history", Frag2);

		Assert.True(RedirectExtension.TryExtractTarget("{{Redirect file:cover-art}}", out string Target3, out string? Frag3));
		Assert.Equal("cover-art", Target3);
		Assert.Null(Frag3);

		string CodeOnly = """
			`{{Redirect inline-code}}`

			```markdown
			{{Redirect fenced-code}}
			```
			""";
		Assert.False(RedirectExtension.TryExtractTarget(CodeOnly, out _, out _));

		Assert.False(RedirectExtension.TryExtractTarget("{{Redirect }}", out _, out _));
		Assert.False(RedirectExtension.TryExtractTarget("{{Redirect https://evil.test/phish}}", out _, out _));
		Assert.False(RedirectExtension.TryExtractTarget("{{Redirect javascript:alert(1)}}", out _, out _));
		Assert.False(RedirectExtension.TryExtractTarget("{{Redirect ../parent}}", out _, out _));
		Assert.False(RedirectExtension.TryExtractTarget("{{Redirect @Editor}}", out _, out _));
		Assert.False(RedirectExtension.TryExtractTarget("{{Redirect info:sitemap}}", out _, out _));
		Assert.False(RedirectExtension.TryExtractTarget("{{Redirect 1994#bad frag}}", out _, out _));
	}

	/// <summary>
	/// Verifies visiting a redirect issues a query-free 302 redirect and shows a one-time TempData banner only on the destination page.
	/// </summary>
	/// <returns>A task that completes when the end-to-end redirect assertions finish.</returns>
	[Fact]
	public async Task RedirectPageIssuesClean302AndShowsOneTimeTempDataNoticeOnTarget()
	{
		await SeedArticleAsync("1994", "1994", ArticleType.Article, "History of *1994* magazine.");
		await SeedArticleAsync("other-article", "Other Article", ArticleType.Article, "Completely unrelated page.");
		await SeedArticleAsync("1984", "1984", ArticleType.Redirect, "{{Redirect 1994}}");

		StubTempDataProvider TempDataStore = new();
		ViewModel RedirectViewer = CreateViewer("1984", TempDataStore);
		IActionResult FirstResult = await RedirectViewer.OnGetAsync(default);

		RedirectResult HttpRedirect = Assert.IsType<RedirectResult>(FirstResult);
		Assert.False(HttpRedirect.Permanent);
		Assert.Equal("/1994", HttpRedirect.Url);
		Assert.DoesNotContain('?', HttpRedirect.Url);
		RedirectViewer.TempData.Save();

		// Navigating to an unrelated article consumes TempData without showing a stale banner.
		ViewModel UnrelatedViewer = CreateViewer("other-article", TempDataStore);
		Assert.IsType<PageResult>(await UnrelatedViewer.OnGetAsync(default));
		Assert.Null(UnrelatedViewer.RedirectedFromTitle);
		Assert.Null(UnrelatedViewer.RedirectedFromEditPath);

		// Re-run redirect cleanly and follow to destination /1994.
		RedirectViewer = CreateViewer("1984", TempDataStore);
		Assert.IsType<RedirectResult>(await RedirectViewer.OnGetAsync(default));
		RedirectViewer.TempData.Save();

		ViewModel TargetViewer = CreateViewer("1994", TempDataStore);
		Assert.IsType<PageResult>(await TargetViewer.OnGetAsync(default));
		Assert.Equal("1984", TargetViewer.RedirectedFromTitle);
		Assert.Equal("/1984/edit", TargetViewer.RedirectedFromEditPath);
		TargetViewer.TempData.Save();

		// Refreshing or visiting /1994 again does not repeat the flash banner.
		ViewModel RepeatViewer = CreateViewer("1994", TempDataStore);
		Assert.IsType<PageResult>(await RepeatViewer.OnGetAsync(default));
		Assert.Null(RepeatViewer.RedirectedFromTitle);
	}

	/// <summary>
	/// Verifies fragment preservation and file-namespace canonical path resolution during redirect execution.
	/// </summary>
	/// <returns>A task that completes when assertions finish.</returns>
	[Fact]
	public async Task RedirectPreservesFragmentAndResolvesFileNamespaceTarget()
	{
		await SeedArticleAsync("target-issue", "Target Issue", ArticleType.Article, "## History\nText.");
		await SeedArticleAsync("issue-alias", "Issue Alias", ArticleType.Redirect, "{{Redirect /target-issue#history}}");
		await SeedArticleAsync("cover-art", "Cover Art", ArticleType.File, "{{Source Example|https://example.test}}");
		await SeedArticleAsync("cover-alias", "Cover Alias", ArticleType.Redirect, "{{Redirect file:cover-art}}");

		StubTempDataProvider TempDataStore = new();

		ViewModel FragmentViewer = CreateViewer("issue-alias", TempDataStore);
		RedirectResult FragmentRedirect = Assert.IsType<RedirectResult>(await FragmentViewer.OnGetAsync(default));
		Assert.Equal("/target-issue#history", FragmentRedirect.Url);

		ViewModel FileViewer = CreateViewer("cover-alias", TempDataStore);
		RedirectResult FileRedirect = Assert.IsType<RedirectResult>(await FileViewer.OnGetAsync(default));
		Assert.Equal("/file:cover-art", FileRedirect.Url);
	}

	/// <summary>
	/// Verifies broken targets, chained targets, and historical revisions render the redirect page safely without looping.
	/// </summary>
	/// <returns>A task that completes when assertions finish.</returns>
	[Fact]
	public async Task BrokenChainedAndHistoricalRedirectsDoNot302Redirect()
	{
		await SeedArticleAsync("hop-two", "Hop Two", ArticleType.Redirect, "{{Redirect nonexistent}}");
		await SeedArticleAsync("hop-one", "Hop One", ArticleType.Redirect, "{{Redirect hop-two}}");
		await SeedArticleAsync("valid-target", "Valid Target", ArticleType.Article, "Destination body.");
		DateTimeOffset RevisionDate = await SeedArticleAsync("historical-redirect", "Historical Redirect", ArticleType.Redirect, "{{Redirect valid-target}}");

		StubTempDataProvider TempDataStore = new();

		// Missing target renders notice without 302 into 404.
		ViewModel BrokenViewer = CreateViewer("hop-two", TempDataStore);
		Assert.IsType<PageResult>(await BrokenViewer.OnGetAsync(default));
		Assert.False(BrokenViewer.AllowSearchEngineIndexingOfPage);
		Assert.Contains("does not exist yet", BrokenViewer.ArticleRevisionHtml);

		// Redirect pointing to another redirect does not follow or loop.
		ViewModel ChainedViewer = CreateViewer("hop-one", TempDataStore);
		Assert.IsType<PageResult>(await ChainedViewer.OnGetAsync(default));
		Assert.False(ChainedViewer.AllowSearchEngineIndexingOfPage);
		Assert.Contains("cannot be chained", ChainedViewer.ArticleRevisionHtml);

		// Explicit historical revision renders instead of redirecting.
		ViewModel HistoryViewer = CreateViewer("historical-redirect", TempDataStore);
		HistoryViewer.Revision = RevisionDate.UtcDateTime.ToString("yyyyMMddHHmmssfff");
		Assert.IsType<PageResult>(await HistoryViewer.OnGetAsync(default));
		Assert.Contains("redirect-notice", HistoryViewer.ArticleRevisionHtml);
		Assert.Contains("/valid-target", HistoryViewer.ArticleRevisionHtml);
	}

	/// <summary>
	/// Verifies create/edit workflow validation rejects missing directives, self-redirects, chains, and file uploads.
	/// </summary>
	/// <returns>A task that completes when validation assertions finish.</returns>
	[Fact]
	public async Task CreateEditAndServiceRejectInvalidSelfChainedAndFileRedirects()
	{
		await SeedArticleAsync("1994", "1994", ArticleType.Article, "Destination body.");
		await SeedArticleAsync("already-redirect", "Already Redirect", ArticleType.Redirect, "{{Redirect 1994}}");

		// Missing directive
		CreateEditModel MissingDirective = CreateEditor(null);
		MissingDirective.Title = "Bad Redirect";
		MissingDirective.UrlSlug = "bad-redirect";
		MissingDirective.SelectedType = ArticleType.Redirect;
		MissingDirective.ArticleText = "Just plain prose.";
		Assert.IsType<PageResult>(await MissingDirective.OnPostAsync(default));
		Assert.Contains("{{Redirect target-slug}}", MissingDirective.ErrorMessage);

		// Self-redirect
		CreateEditModel SelfRedirect = CreateEditor(null);
		SelfRedirect.Title = "Self Loop";
		SelfRedirect.UrlSlug = "self-loop";
		SelfRedirect.SelectedType = ArticleType.Redirect;
		SelfRedirect.ArticleText = "{{Redirect self-loop}}";
		Assert.IsType<PageResult>(await SelfRedirect.OnPostAsync(default));
		Assert.Contains("redirect to itself", SelfRedirect.ErrorMessage);

		// Chain to existing redirect
		CreateEditModel ChainedRedirect = CreateEditor(null);
		ChainedRedirect.Title = "Chain";
		ChainedRedirect.UrlSlug = "chain-attempt";
		ChainedRedirect.SelectedType = ArticleType.Redirect;
		ChainedRedirect.ArticleText = "{{Redirect already-redirect}}";
		Assert.IsType<PageResult>(await ChainedRedirect.OnPostAsync(default));
		Assert.Contains("itself a redirect", ChainedRedirect.ErrorMessage);

		// Redirect with file upload
		CreateEditModel FileRedirect = CreateEditor(null);
		FileRedirect.Title = "File Redirect";
		FileRedirect.UrlSlug = "file-redirect";
		FileRedirect.SelectedType = ArticleType.Redirect;
		FileRedirect.ArticleText = "{{Redirect 1994}}";
		using MemoryStream DummyStream = new(new byte[1]);
		FileRedirect.UploadedFile = new FormFile(DummyStream, 0, 1, "UploadedFile", "image.png");
		Assert.IsType<PageResult>(await FileRedirect.OnPostAsync(default));
		Assert.Contains("cannot have an attached file", FileRedirect.ErrorMessage);

		// Valid creation + update validation via ArticleRevisionService
		CreateEditModel ValidCreate = CreateEditor(null);
		ValidCreate.Title = "1984";
		ValidCreate.UrlSlug = "1984";
		ValidCreate.SelectedType = ArticleType.Redirect;
		ValidCreate.ArticleText = "{{Redirect 1994}}";
		RedirectResult Created = Assert.IsType<RedirectResult>(await ValidCreate.OnPostAsync(default));
		Assert.Equal("/1984", Created.Url);

		ArticleRevisionService Revisions = new(Context, NullLogger<ArticleRevisionService>.Instance);
		ArticleRevisionWorkflowException UpdateEx = await Assert.ThrowsAsync<ArticleRevisionWorkflowException>(() =>
			Revisions.UpdateArticleAsync(
				new ArticleRevisionUpdateCommand(
					OriginalUrlSlug: "1984",
					NewUrlSlug: "1984",
					SiteId: 1,
					Culture: "en",
					Title: "1984",
					DisplayTitle: null,
					Type: ArticleType.Redirect,
					CanonicalFileId: null,
					Text: "{{Redirect already-redirect}}",
					RevisionReason: "Bad edit",
					CreatedByUserId: EditorUser.Id),
				default));
		Assert.Equal(ArticleRevisionWorkflowFailureKind.Invalid, UpdateEx.Kind);
	}

	/// <summary>
	/// Verifies redirects match title search as <see cref="ArticleType.Redirect"/> while remaining excluded from body-snippet search.
	/// </summary>
	/// <returns>A task that completes when search assertions finish.</returns>
	[Fact]
	public async Task SearchIncludesRedirectTitlesAndExcludesRedirectBodyDirectives()
	{
		await SeedArticleAsync("1994", "1994", ArticleType.Article, "Warren Publishing renamed the anthology to *1994* in 1983.");
		await SeedArticleAsync("1984", "1984", ArticleType.Redirect, "{{Redirect 1994}}");

		ArticleSearchService Search = new(Context);

		IReadOnlyList<ArticleSearchResult> RedirectTitleHits = await Search.SuggestAsync("1984", default);
		ArticleSearchResult FirstHit = Assert.Single(RedirectTitleHits);
		Assert.Equal("1984", FirstHit.Title);
		Assert.Equal(ArticleType.Redirect, FirstHit.Type);
		Assert.True(FirstHit.IsTitleMatch);

		// Searching "1994" must not surface /1984 via its "{{Redirect 1994}}" directive body text.
		IReadOnlyList<ArticleSearchResult> TargetSearchHits = await Search.SearchAsync("1994", default);
		ArticleSearchResult TargetHit = Assert.Single(TargetSearchHits);
		Assert.Equal("1994", TargetHit.Title);
		Assert.Equal(ArticleType.Article, TargetHit.Type);
	}

	/// <summary>
	/// Seeds a current article revision in SQLite and returns its creation timestamp.
	/// </summary>
	private async Task<DateTimeOffset> SeedArticleAsync(string Slug, string Title, ArticleType Type, string Text)
	{
		DateTimeOffset Now = DateTimeOffset.UtcNow;
		Context.ArticleRevisions.Add(new ArticleRevision
		{
			CanonicalArticleId = Guid.NewGuid(),
			SiteId = 1,
			Culture = "en",
			Title = Title,
			UrlSlug = Slug,
			Type = Type,
			Text = Text,
			IsCurrent = true,
			RevisionReason = "Seeded",
			CreatedByUserId = EditorUser.Id,
			DateCreated = Now
		});
		await Context.SaveChangesAsync();
		return Now;
	}

	/// <summary>
	/// Creates an initialized <see cref="ViewModel"/> backed by the given <see cref="ITempDataProvider"/>.
	/// </summary>
	private ViewModel CreateViewer(string Slug, ITempDataProvider TempDataStore)
	{
		DefaultHttpContext Http = CreateHttpContext();
		SiteResolverService Resolver = new(Configuration, new HttpContextAccessor { HttpContext = Http });
		ArticleRevisionService Revisions = new(Context, NullLogger<ArticleRevisionService>.Instance);
		ArticleRenderer Renderer = new(Context, new MarkdownPipelineFactory(Configuration), Configuration);
		ViewModel Viewer = new(Context, Resolver, Users, Renderer, Revisions, new(Context)) { UrlSlug = Slug };
		Viewer.PageContext = new() { HttpContext = Http, RouteData = new(), ActionDescriptor = new() };
		Viewer.TempData = new TempDataDictionary(Http, TempDataStore);
		Viewer.OnPageHandlerExecuting(new(Viewer.PageContext, [], new HandlerMethodDescriptor(), new Dictionary<string, object?>(), Viewer));
		return Viewer;
	}

	/// <summary>
	/// Creates an initialized <see cref="CreateEditModel"/> for editor validation tests.
	/// </summary>
	private CreateEditModel CreateEditor(string? Slug)
	{
		DefaultHttpContext Http = CreateHttpContext();
		SiteResolverService Resolver = new(Configuration, new HttpContextAccessor { HttpContext = Http });
		CreateEditModel Page = new(
			Context,
			Users,
			Resolver,
			new(Context, NullLogger<ArticleRevisionService>.Instance),
			new(Context, Options.Create(new FileStorageOptions { SiteFilesPath = StorageRoot })))
		{
			UrlSlug = Slug
		};
		Page.PageContext = new() { HttpContext = Http, RouteData = new(), ActionDescriptor = new() };
		Page.OnPageHandlerExecuting(new(Page.PageContext, [], new HandlerMethodDescriptor(), new Dictionary<string, object?>(), Page));
		return Page;
	}

	/// <summary>
	/// Creates a test <see cref="DefaultHttpContext"/> with configured host and authenticated claims.
	/// </summary>
	private DefaultHttpContext CreateHttpContext()
	{
		DefaultHttpContext Http = new();
		Http.Request.Host = new("en.example.test");
		Http.User = new(new ClaimsIdentity(
			[new(ClaimTypes.NameIdentifier, EditorUser.Id.ToString()), new(ClaimTypes.Name, EditorUser.UserName!)],
			"Test"));
		return Http;
	}

	/// <summary>
	/// In-memory <see cref="ITempDataProvider"/> simulating per-request cookie round-trips across separate handlers.
	/// </summary>
	private sealed class StubTempDataProvider : ITempDataProvider
	{
		private IDictionary<string, object?> StoredValues = new Dictionary<string, object?>(StringComparer.Ordinal);

		/// <inheritdoc/>
		public IDictionary<string, object?> LoadTempData(HttpContext Context)
			=> new Dictionary<string, object?>(StoredValues, StringComparer.Ordinal);

		/// <inheritdoc/>
		public void SaveTempData(HttpContext Context, IDictionary<string, object?> Values)
			=> StoredValues = new Dictionary<string, object?>(Values, StringComparer.Ordinal);
	}
}
