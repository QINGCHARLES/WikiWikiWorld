using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.MarkdigExtensions;
using WikiWikiWorld.Web.Pages;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies header directive extraction and SQLite-backed front-page selection across covers and fallback headers.
/// </summary>
public sealed class HomePageTests : IDisposable
{
	private readonly SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
	private readonly WikiWikiWorldDbContext Context;
	private readonly Guid EditorId = Guid.NewGuid();
	private readonly IOptions<SiteConfiguration> Configuration = Options.Create(new SiteConfiguration
	{
		Sites =
		[
			new() { SiteId = 1, SiteName = "Magazedia", Domains = ["magazedia.test", "en.magazedia.test", "fr.magazedia.test"], RootDomainIsCultureSelectorOnly = true },
			new() { SiteId = 2, SiteName = "Other", Domains = ["other.test"], DefaultCulture = "en" }
		]
	});

	/// <summary>
	/// Creates an isolated in-memory SQLite database with real schema constraints.
	/// </summary>
	public HomePageTests()
	{
		Connection.Open();
		Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		Context.Database.EnsureCreated();
		Context.Users.Add(new User { Id = EditorId, UserName = "Editor", NormalizedUserName = "EDITOR" });
		Context.SaveChanges();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Context.Dispose();
		Connection.Dispose();
	}

	/// <summary>
	/// Ensures <see cref="ArticleCoverReference.ExtractHeaders"/> matches <see cref="HeaderImageBlockParser"/>
	/// for valid directives, focus tokens, leading spaces, and code-block exclusions.
	/// </summary>
	/// <param name="Source">The Markdown source to inspect.</param>
	/// <param name="ExpectedSlug">The expected single slug, or <see langword="null"/> when none should match.</param>
	/// <param name="ExpectedFocus">The expected normalized focus token.</param>
	[Theory]
	[InlineData("{{HeaderImage file:header-for-playboy}}", "header-for-playboy", null)]
	[InlineData("{{HeaderImage playboy-banner |#| Focus=centre |#| AltText=Playboy banner}}", "playboy-banner", "center")]
	[InlineData("   {{HeaderImage file:indented-three |#| Focus=bottom}}", "indented-three", "bottom")]
	[InlineData("{{HeaderImage art |#| Focus=TOP}}", "art", "top")]
	[InlineData("{{HeaderImage art |#| Focus=sideways}}", "art", null)]
	[InlineData("    {{HeaderImage file:indented-code}}", null, null)]
	[InlineData("\t{{HeaderImage file:tab-indented-code}}", null, null)]
	[InlineData("```markdown\n{{HeaderImage file:fenced-example}}\n```", null, null)]
	[InlineData("~~~\n{{HeaderImage file:tilde-fenced}}\n~~~", null, null)]
	[InlineData("{{HeaderImageExtra file:wrong}}", null, null)]
	[InlineData("{{HeaderImage file:unfinished", null, null)]
	[InlineData("{{HeaderImage   }}", null, null)]
	public void ExtractHeadersHandlesSyntaxFocusAndCodeBlocks(string Source, string? ExpectedSlug, string? ExpectedFocus)
	{
		IReadOnlyList<HeaderImageReference> Headers = ArticleCoverReference.ExtractHeaders(Source);
		if (ExpectedSlug is null)
		{
			Assert.Empty(Headers);
			return;
		}

		HeaderImageReference Header = Assert.Single(Headers);
		Assert.Equal(ExpectedSlug, Header.Slug);
		Assert.Equal(ExpectedFocus, Header.Focus);
	}

	/// <summary>
	/// Verifies that the home page prefers resolved covers, falls back to resolved headers (preserving focus),
	/// orders by revision creation date, and scopes strictly to current standard articles in the active site and culture.
	/// Checks that grid entries are classified separately from their artwork and receive the configured logo or fitted text.
	/// </summary>
	/// <returns>A task that completes when SQLite query and fallback assertions finish.</returns>
	[Fact]
	public async Task HomePageSelectsCoversAndFallbackHeadersWithinSiteAndCultureScope()
	{
		DateTimeOffset BaseTime = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

		string CoverIssueUrl = SeedImageFile("cover-of-wired-1", "wired-1-cover.jpg");
		string BothCoverUrl = SeedImageFile("cover-of-032c-1", "032c-1-cover.jpg");
		SeedImageFile("header-for-032c-1", "032c-1-header.avif");
		string PlayboyHeaderUrl = SeedImageFile("header-for-playboy", "playboy-header.avif");
		string PlayboyLogoUrl = SeedImageFile("playboy-logo", "playboy-logo.svg");
		string BackupHeaderUrl = SeedImageFile("header-for-george-backup", "george-header.avif");
		string FallbackHeaderUrl = SeedImageFile("header-for-broken-cover-issue", "broken-cover-header.avif");
		SeedImageFile("deleted-file-slug", "deleted.avif", IsDeletedFile: true);

		// Populate >50 newer rows in another site/culture to ensure SiteId/Culture filtering happens before Take(50).
		string OtherSiteCoverSlug = "other-site-cover";
		Guid OtherFileId = Guid.NewGuid();
		Context.FileRevisions.Add(new FileRevision
		{
			CanonicalFileId = OtherFileId,
			IsCurrent = true,
			Type = FileType.Image2D,
			Filename = "other.jpg",
			MimeType = "image/jpeg",
			FileSizeBytes = 100,
			RevisionReason = "Seeded",
			SourceAndRevisionReasonCulture = "en",
			CreatedByUserId = EditorId,
			DateCreated = BaseTime
		});
		Context.ArticleRevisions.Add(new ArticleRevision
		{
			CanonicalArticleId = Guid.NewGuid(),
			CanonicalFileId = OtherFileId,
			SiteId = 2,
			Culture = "en",
			Title = "File:Other",
			UrlSlug = OtherSiteCoverSlug,
			Type = ArticleType.File,
			Text = "File",
			IsCurrent = true,
			RevisionReason = "Seeded",
			CreatedByUserId = EditorId,
			DateCreated = BaseTime
		});
		for (int i = 0; i < 55; i++)
		{
			AddArticle(
				SiteId: 2,
				Culture: "en",
				Slug: $"other-issue-{i}",
				Title: $"Other Issue {i}",
				Text: $"{{{{CoverCarousel CoverImage=file:{OtherSiteCoverSlug}}}}}",
				Created: BaseTime.AddHours(10 + i));
		}

		// Excluded rows in Site 1: French culture, prior revision, deleted revision, deleted file, and File namespace.
		AddArticle(1, "fr", "french-playboy", "Playboy FR", "{{HeaderImage file:header-for-playboy}}", BaseTime.AddHours(5));
		AddArticle(1, "en", "prior-playboy", "Old Revision", "{{HeaderImage file:header-for-playboy}}", BaseTime.AddHours(5), IsCurrent: false);
		AddArticle(1, "en", "deleted-playboy", "Deleted Article", "{{HeaderImage file:header-for-playboy}}", BaseTime.AddHours(5), IsDeleted: true);
		AddArticle(1, "en", "deleted-file-article", "Deleted File Ref", "{{HeaderImage file:deleted-file-slug}}", BaseTime.AddHours(5));
		AddArticle(1, "en", "file-typed-page", "File Namespace", "{{HeaderImage file:header-for-playboy}}", BaseTime.AddHours(5), Type: ArticleType.File);

		// Valid Site 1 English articles in chronological order:
		AddArticle(
			1, "en", "wired-1", "Wired Issue 1",
			"{{CoverCarousel CoverImage=file:cover-of-wired-1}}",
			BaseTime.AddMinutes(1));
		AddArticle(
			1, "en", "playboy", "Playboy",
			"{{ShortDescription American men's lifestyle and entertainment magazine}}\n{{Eyebrow MAGAZINE}}\n{{HeaderImage file:header-for-playboy |#| Focus=centre}}",
			BaseTime.AddMinutes(2), DisplayTitle: "[[Playboy|#|playboy-logo]]");
		AddArticle(
			1, "en", "032c-no-1", "032c - No. 1",
			"{{Eyebrow MAGAZINEISSUE}}\n{{HeaderImage file:header-for-032c-1 |#| Focus=bottom}}\n{{CoverCarousel CoverImage=file:cover-of-032c-1}}",
			BaseTime.AddMinutes(3));
		AddArticle(
			1, "en", "george", "George",
			"{{HeaderImage file:missing-first-header |#| Focus=top}}\n{{HeaderImage file:header-for-george-backup |#| Focus=bottom}}",
			BaseTime.AddMinutes(4), DisplayTitle: "[[George|#|missing-george-logo]]");
		AddArticle(
			1, "en", "issue-with-missing-cover", "Issue With Missing Cover",
			"{{HeaderImage file:header-for-broken-cover-issue}}\n{{CoverCarousel CoverImage=file:does-not-exist}}",
			BaseTime.AddMinutes(5), DisplayTitle: "[[Playboy|#|playboy-logo]]\nIssue 1");
		AddArticle(
			1, "en", "header-only-issue", "Header Only Issue",
			"{{HeaderImage file:header-for-broken-cover-issue}}\n{{PublicationIssueInfobox CoverDate=2024-03}}",
			BaseTime.AddMinutes(6));
		AddArticle(
			1, "en", "wired", "Wired",
			"{{CoverCarousel CoverImage=file:cover-of-wired-1}}\n{{Infobox Publication ISSN=1059-1028}}",
			BaseTime.AddMinutes(7));

		await Context.SaveChangesAsync();

		IndexModel Page = CreateHomePage("en.magazedia.test");
		await Page.OnGetAsync(default);

		Assert.Collection(
			Page.ArticlesWithCovers,
			Item =>
			{
				Assert.Equal("wired", Item.UrlSlug);
				Assert.Equal(CoverIssueUrl, Item.ImageUrl);
				Assert.Equal(HomeCoverKind.Publication, Item.Kind);
				Assert.Equal("Wired", Item.TitleHtml);
				Assert.Contains("--title-word-chars: 5", Item.TitleFitStyle);
			},
			Item =>
			{
				Assert.Equal("header-only-issue", Item.UrlSlug);
				Assert.Equal(FallbackHeaderUrl, Item.ImageUrl);
				Assert.Equal(HomeCoverKind.Issue, Item.Kind);
				Assert.Equal("Header Only Issue", Item.TitleHtml);
				Assert.Contains("--title-word-chars: 6", Item.TitleFitStyle);
			},
			Item =>
			{
				Assert.Equal("issue-with-missing-cover", Item.UrlSlug);
				Assert.Equal(FallbackHeaderUrl, Item.ImageUrl);
				Assert.True(Item.IsHeaderImage);
				Assert.Null(Item.ImageFocus);
				Assert.Equal(HomeCoverKind.Issue, Item.Kind);
				Assert.Contains(PlayboyLogoUrl, Item.TitleHtml);
				Assert.EndsWith("Issue 1", Item.TitleHtml);
			},
			Item =>
			{
				Assert.Equal("george", Item.UrlSlug);
				Assert.Equal(BackupHeaderUrl, Item.ImageUrl);
				Assert.True(Item.IsHeaderImage);
				Assert.Equal("bottom", Item.ImageFocus);
				Assert.Equal(HomeCoverKind.Article, Item.Kind);
				Assert.Equal("George", Item.TitleHtml);
			},
			Item =>
			{
				Assert.Equal("032c-no-1", Item.UrlSlug);
				Assert.Equal(BothCoverUrl, Item.ImageUrl);
				Assert.False(Item.IsHeaderImage);
				Assert.Null(Item.ImageFocus);
				Assert.Equal(HomeCoverKind.Issue, Item.Kind);
			},
			Item =>
			{
				Assert.Equal("playboy", Item.UrlSlug);
				Assert.Equal(PlayboyHeaderUrl, Item.ImageUrl);
				Assert.True(Item.IsHeaderImage);
				Assert.Equal("center", Item.ImageFocus);
				Assert.Equal(HomeCoverKind.Publication, Item.Kind);
				Assert.Contains(PlayboyLogoUrl, Item.TitleHtml);
				Assert.Null(Item.TitleFitStyle);
			},
			Item =>
			{
				Assert.Equal("wired-1", Item.UrlSlug);
				Assert.Equal(CoverIssueUrl, Item.ImageUrl);
				Assert.False(Item.IsHeaderImage);
				Assert.Null(Item.ImageFocus);
				Assert.Equal(HomeCoverKind.Issue, Item.Kind);
				Assert.Equal("Wired Issue 1", Item.TitleHtml);
			});
	}

	/// <summary>
	/// Seeds a file article and its current <see cref="FileRevision"/> in Site 1 English, returning the expected public URL.
	/// </summary>
	/// <param name="FileSlug">The file article's URL slug.</param>
	/// <param name="Filename">The stored filename.</param>
	/// <param name="IsDeletedFile">Whether to mark the file revision soft-deleted.</param>
	/// <returns>The expected public image URL for the seeded revision.</returns>
	private string SeedImageFile(string FileSlug, string Filename, bool IsDeletedFile = false)
	{
		Guid CanonicalFileId = Guid.NewGuid();
		DateTimeOffset Now = DateTimeOffset.UtcNow;
		FileRevision FileRev = new()
		{
			CanonicalFileId = CanonicalFileId,
			IsCurrent = true,
			Type = FileType.Image2D,
			Filename = Filename,
			MimeType = "image/avif",
			FileSizeBytes = 1024,
			RevisionReason = "Seeded",
			SourceAndRevisionReasonCulture = "en",
			CreatedByUserId = EditorId,
			DateCreated = Now,
			DateDeleted = IsDeletedFile ? Now : null
		};
		Context.FileRevisions.Add(FileRev);
		Context.ArticleRevisions.Add(new ArticleRevision
		{
			CanonicalArticleId = Guid.NewGuid(),
			CanonicalFileId = CanonicalFileId,
			SiteId = 1,
			Culture = "en",
			Title = $"File:{FileSlug}",
			UrlSlug = FileSlug,
			Type = ArticleType.File,
			Text = "Seeded file article",
			IsCurrent = true,
			RevisionReason = "Seeded",
			CreatedByUserId = EditorId,
			DateCreated = Now
		});
		Context.SaveChanges();
		return Helpers.ImageUrlHelper.BuildImageUrl(1, CanonicalFileId, FileRev.Id, Filename);
	}

	/// <summary>
	/// Queues an <see cref="ArticleRevision"/> in the test database context.
	/// </summary>
	/// <param name="SiteId">The owning site ID.</param>
	/// <param name="Culture">The article culture.</param>
	/// <param name="Slug">The URL slug.</param>
	/// <param name="Title">The display title.</param>
	/// <param name="Text">The Markdown text.</param>
	/// <param name="Created">The revision timestamp.</param>
	/// <param name="IsCurrent">Whether the revision is current.</param>
	/// <param name="IsDeleted">Whether the revision is soft-deleted.</param>
	/// <param name="Type">The article namespace type.</param>
	/// <param name="DisplayTitle">An optional authored heading, including logo tokens.</param>
	private void AddArticle(
		int SiteId,
		string Culture,
		string Slug,
		string Title,
		string Text,
		DateTimeOffset Created,
		bool IsCurrent = true,
		bool IsDeleted = false,
		ArticleType Type = ArticleType.Article,
		string? DisplayTitle = null)
	{
		Context.ArticleRevisions.Add(new ArticleRevision
		{
			CanonicalArticleId = Guid.NewGuid(),
			SiteId = SiteId,
			Culture = Culture,
			Title = Title,
			DisplayTitle = DisplayTitle,
			UrlSlug = Slug,
			Type = Type,
			Text = Text,
			IsCurrent = IsCurrent,
			RevisionReason = "Seeded",
			CreatedByUserId = EditorId,
			DateCreated = Created,
			DateDeleted = IsDeleted ? Created : null
		});
	}

	/// <summary>
	/// Creates an initialized <see cref="IndexModel"/> bound to <paramref name="Host"/>.
	/// </summary>
	/// <param name="Host">The request host header.</param>
	/// <returns>The initialized page model.</returns>
	private IndexModel CreateHomePage(string Host)
	{
		DefaultHttpContext Http = new();
		Http.Request.Host = new HostString(Host);
		SiteResolverService Resolver = new(Configuration, new HttpContextAccessor { HttpContext = Http });
		IndexModel Page = new(Context, Resolver)
		{
			PageContext = new() { HttpContext = Http, RouteData = new(), ActionDescriptor = new() }
		};
		Page.OnPageHandlerExecuting(new(Page.PageContext, [], new HandlerMethodDescriptor(), new Dictionary<string, object?>(), Page));
		return Page;
	}
}
