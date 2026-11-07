using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Pages;
using WikiWikiWorld.Web.Pages.Article;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Exercises missing-user-page creation and normal editing through real Razor handlers and SQLite.</summary>
public sealed class UserArticleEditorTests : IDisposable
{
	private readonly SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
	private readonly WikiWikiWorldDbContext Context;
	private readonly UserManager<User> Users;
	private readonly string StorageRoot = Path.Combine(Path.GetTempPath(), $"WikiWikiWorld.EditorTests-{Guid.NewGuid():N}");
	private readonly User Owner = new() { Id = Guid.NewGuid(), UserName = "Owner", NormalizedUserName = "OWNER" };
	private readonly IOptions<SiteConfiguration> Configuration = Options.Create(new SiteConfiguration
	{
		Sites = [new() { SiteId = 1, Domains = ["example.test"], DefaultCulture = "en" }]
	});

	/// <summary>Creates an isolated account and database without provisioning its user article.</summary>
	public UserArticleEditorTests()
	{
		Connection.Open();
		Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		Context.Database.EnsureCreated();
		Context.Users.Add(Owner);
		Context.SaveChanges();
		Users = new(new UserStore<User, Role, WikiWikiWorldDbContext, Guid>(Context), Options.Create(new IdentityOptions()),
			new PasswordHasher<User>(), [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!, NullLogger<UserManager<User>>.Instance);
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

	/// <summary>A registered owner can reach a genuine creation form for a missing user article.</summary>
	/// <returns>A task that completes when the scenario's workflow assertions finish.</returns>
	[Fact]
	public async Task MissingOwnUserPageOpensCreationForm()
	{
		CreateEditModel Page = CreateEditor("@Owner");
		Assert.IsType<PageResult>(await Page.OnGetAsync());
		Assert.False(Page.IsEditMode);
		Assert.Empty(Page.OriginalUrlSlug);
		Assert.Equal("@Owner", Page.Title);
		Assert.Equal(ArticleType.User, Page.SelectedType);
		Assert.Empty(Context.ArticleRevisions);
	}

	/// <summary>A typed slug on an invalid creation POST must not become an edit or discard the draft.</summary>
	/// <returns>A task that completes when the scenario's workflow assertions finish.</returns>
	[Fact]
	public async Task CreationValidationFailureKeepsCreateModeAndDraft()
	{
		CreateEditModel Page = CreateEditor("new-article");
		Page.Title = string.Empty;
		Page.ArticleText = "Keep this draft";
		Assert.IsType<PageResult>(await Page.OnPostAsync(default));
		Assert.False(Page.IsEditMode);
		Assert.Empty(Page.OriginalUrlSlug);
		Assert.Equal("Keep this draft", Page.ArticleText);
	}

	/// <summary>The owner can submit the fallback form, including after correcting an invalid draft.</summary>
	/// <returns>A task that completes when the scenario's workflow assertions finish.</returns>
	[Fact]
	public async Task OwnUserPageCreatesAfterValidationFailureAndForcesUserType()
	{
		CreateEditModel Page = CreateEditor("@owner");
		Assert.IsType<PageResult>(await Page.OnGetAsync());
		Assert.Equal("@Owner", Page.UrlSlug);
		Page.Title = "My page";
		Page.DisplayTitle = "My display title";
		Page.SelectedType = ArticleType.File;
		Page.ArticleText = string.Empty;
		Assert.IsType<PageResult>(await Page.OnPostAsync(default));
		Assert.False(Page.IsEditMode);
		Assert.Equal(ArticleType.User, Page.SelectedType);
		Assert.Equal("My display title", Page.DisplayTitle);
		Page.ArticleText = "My corrected draft";
		Assert.IsType<RedirectResult>(await Page.OnPostAsync(default));
		ArticleRevision Created = await Context.ArticleRevisions.AsNoTracking().SingleAsync();
		Assert.Equal(ArticleType.User, Created.Type);
		Assert.Equal("@Owner", Created.UrlSlug);
		Assert.Equal("My corrected draft", Created.Text);
	}

	/// <summary>Neither viewing nor posting a missing foreign user page grants permission to create it.</summary>
	/// <returns>A task that completes when the scenario's workflow assertions finish.</returns>
	[Fact]
	public async Task AnotherUsersMissingPageIsRefusedOnGetAndPost()
	{
		CreateEditModel Page = CreateEditor("@SomeoneElse");
		Assert.IsType<ForbidResult>(await Page.OnGetAsync());
		Page.Title = "Another user";
		Page.ArticleText = "Not allowed";
		Assert.IsType<ForbidResult>(await Page.OnPostAsync(default));
		Assert.Empty(Context.ArticleRevisions);
	}

	/// <summary>Direct handler calls preserve the page's authentication boundary on reads and writes.</summary>
	/// <returns>A task that completes when the scenario's workflow assertions finish.</returns>
	[Fact]
	public async Task AnonymousEditorRequestsAreChallenged()
	{
		CreateEditModel Page = CreateEditor("@Owner");
		Page.HttpContext.User = new(new ClaimsIdentity());
		Assert.IsType<ChallengeResult>(await Page.OnGetAsync());
		Assert.IsType<ChallengeResult>(await Page.OnPostAsync(default));
		Assert.Empty(Context.ArticleRevisions);
	}

	/// <summary>Missing ordinary articles still return 404, while the normal blank creation form remains usable.</summary>
	/// <returns>A task that completes when the scenario's workflow assertions finish.</returns>
	[Fact]
	public async Task NormalArticleCreationAndMissingEditBehaviorArePreserved()
	{
		Assert.IsType<NotFoundObjectResult>(await CreateEditor("missing-article").OnGetAsync());
		CreateEditModel Page = CreateEditor(null);
		Assert.IsType<PageResult>(await Page.OnGetAsync());
		Assert.False(Page.IsEditMode);
		Page.Title = "New article";
		Page.UrlSlug = "new-article";
		Page.ArticleText = "New article text";
		Assert.IsType<RedirectResult>(await Page.OnPostAsync(default));
		Assert.Equal(ArticleType.Article, (await Context.ArticleRevisions.SingleAsync()).Type);
	}

	/// <summary>Loaded ordinary and user articles retain their identity and edit mode through invalid and corrected posts.</summary>
	/// <param name="Slug">The existing article slug.</param>
	/// <param name="Type">Its existing namespace.</param>
	/// <returns>A task that completes when the scenario's workflow assertions finish.</returns>
	[Theory]
	[InlineData("@Owner", ArticleType.User)]
	[InlineData("existing-article", ArticleType.Article)]
	public async Task ExistingArticlesRemainEdits(string Slug, ArticleType Type)
	{
		await AddArticleAsync(Slug, Type);
		CreateEditModel Page = CreateEditor(Slug);
		Assert.IsType<PageResult>(await Page.OnGetAsync());
		Assert.True(Page.IsEditMode);
		Assert.Equal(Slug, Page.OriginalUrlSlug);
		Page.Title = string.Empty;
		Page.ArticleText = "Edited draft";
		Assert.IsType<PageResult>(await Page.OnPostAsync(default));
		Assert.True(Page.IsEditMode);
		Assert.Equal("Edited draft", Page.ArticleText);
		Page.Title = "Changed title";
		Assert.IsType<RedirectResult>(await Page.OnPostAsync(default));
		Assert.Equal(2, await Context.ArticleRevisions.CountAsync());
		Assert.Equal("Edited draft", (await Context.ArticleRevisions.SingleAsync(r => r.IsCurrent)).Text);
	}

	/// <summary>Legacy bare file slugs still redirect to the namespaced editor before rendering.</summary>
	/// <returns>A task that completes when the scenario's workflow assertions finish.</returns>
	[Fact]
	public async Task FileEditorCanonicalRedirectIsPreserved()
	{
		await AddArticleAsync("picture", ArticleType.File);
		RedirectResult Redirect = Assert.IsType<RedirectResult>(await CreateEditor("picture").OnGetAsync());
		Assert.True(Redirect.Permanent);
		Assert.Equal("/file:picture/edit", Redirect.Url);
	}

	/// <summary>An attachment cannot change the forced User namespace when submitting the new fallback form.</summary>
	/// <returns>A task that completes when the scenario's workflow assertions finish.</returns>
	[Fact]
	public async Task UserPageAttachmentCannotConvertItToFileArticle()
	{
		CreateEditModel Page = CreateEditor("@Owner");
		await Page.OnGetAsync();
		Page.ArticleText = "My user page";
		using MemoryStream Input = new(new byte[1]);
		Page.UploadedFile = new FormFile(Input, 0, 1, "UploadedFile", "picture.svg");
		Assert.IsType<PageResult>(await Page.OnPostAsync(default));
		Assert.Equal(ArticleType.User, Page.SelectedType);
		Assert.Contains("attached file", Page.ErrorMessage);
		Assert.Empty(Context.ArticleRevisions);
		Assert.Empty(Context.FileRevisions);
		Assert.False(Directory.Exists(StorageRoot));
	}

	/// <summary>A failed best-effort viewer save redirects to an owner form that can actually create the page afterward.</summary>
	/// <returns>A task that completes when the scenario's workflow assertions finish.</returns>
	[Fact]
	public async Task ViewerProvisioningFailureLeadsToUsableOwnerFallback()
	{
		DefaultHttpContext Http = CreateHttpContext();
		SiteResolverService Resolver = new(Configuration, new HttpContextAccessor { HttpContext = Http });
		ArticleRevisionService Revisions = new(Context, NullLogger<ArticleRevisionService>.Instance);
		ViewModel Viewer = new(Context, Resolver, Users,
			new ArticleRenderer(Context, new MarkdownPipelineFactory(Configuration), Configuration), Revisions, new(Context)) { UrlSlug = "@Owner" };
		InitializePage(Viewer, Http);
		EventHandler<SavingChangesEventArgs> FailProvisioning = (_, _) => throw new InvalidOperationException("Simulated provisioning save failure.");
		Context.SavingChanges += FailProvisioning;
		RedirectResult Redirect;
		try
		{
			Redirect = Assert.IsType<RedirectResult>(await Viewer.OnGetAsync(default));
		}
		finally
		{
			Context.SavingChanges -= FailProvisioning;
		}
		Uri Destination = new(new Uri("https://en.example.test"), Redirect.Url);
		CreateEditModel Editor = CreateEditor(QueryHelpers.ParseQuery(Destination.Query)["urlslug"].ToString());
		Assert.IsType<PageResult>(await Editor.OnGetAsync());
		Assert.False(Editor.IsEditMode);
		Editor.ArticleText = "Manually created after provisioning failed";
		Assert.IsType<RedirectResult>(await Editor.OnPostAsync(default));
		Assert.Equal(Editor.ArticleText, (await Context.ArticleRevisions.SingleAsync()).Text);
	}

	/// <summary>Persists an existing current article for normal edit and canonical-route regression coverage.</summary>
	/// <param name="Slug">The stored bare slug.</param>
	/// <param name="Type">The stored article namespace.</param>
	/// <returns>A task completing after the article is saved.</returns>
	private async Task AddArticleAsync(string Slug, ArticleType Type)
	{
		Context.ArticleRevisions.Add(new()
		{
			CanonicalArticleId = Guid.NewGuid(), SiteId = 1, Culture = "en", Title = "Existing title", UrlSlug = Slug,
			Type = Type, Text = "Existing text", IsCurrent = true, RevisionReason = "Seeded", CreatedByUserId = Owner.Id,
			DateCreated = DateTimeOffset.UtcNow
		});
		await Context.SaveChangesAsync();
	}

	/// <summary>Builds an editor with the same services used by upload tests, without touching physical storage.</summary>
	/// <param name="Slug">The requested or submitted slug, if any.</param>
	/// <returns>The initialized Razor handler and its authenticated owner context.</returns>
	private CreateEditModel CreateEditor(string? Slug)
	{
		DefaultHttpContext Http = CreateHttpContext();
		SiteResolverService Resolver = new(Configuration, new HttpContextAccessor { HttpContext = Http });
		CreateEditModel Page = new(Context, Users, Resolver, new(Context, NullLogger<ArticleRevisionService>.Instance),
			new(Context, Options.Create(new FileStorageOptions { SiteFilesPath = StorageRoot }))) { UrlSlug = Slug };
		InitializePage(Page, Http);
		return Page;
	}

	/// <summary>Creates the owner's ordinary cookie-style principal over the test site.</summary>
	/// <returns>An HTTP context with valid site resolution and Identity claims.</returns>
	private DefaultHttpContext CreateHttpContext()
	{
		DefaultHttpContext Http = new();
		Http.Request.Host = new("en.example.test");
		Http.User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, Owner.Id.ToString()), new(ClaimTypes.Name, Owner.UserName!)], "Test"));
		return Http;
	}

	/// <summary>Runs the shared site-resolution filter just as Razor does before invoking a handler.</summary>
	/// <param name="Page">The handler to initialize.</param>
	/// <param name="Http">The request context.</param>
	private static void InitializePage(BasePageModel Page, DefaultHttpContext Http)
	{
		Page.PageContext = new() { HttpContext = Http, RouteData = new(), ActionDescriptor = new() };
		Page.OnPageHandlerExecuting(new(Page.PageContext, [], new HandlerMethodDescriptor(), new Dictionary<string, object?>(), Page));
	}
}
