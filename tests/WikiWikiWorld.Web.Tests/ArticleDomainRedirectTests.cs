using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies the domain redirect resolution, persistence, and article rename survival.
/// </summary>
public sealed class ArticleDomainRedirectTests : IDisposable
{
	private readonly SqliteConnection Connection;
	private readonly DbContextOptions<WikiWikiWorldDbContext> DbOptions;
	private readonly IMemoryCache MemoryCache;
	private readonly IOptions<SiteConfiguration> SiteConfigurationOptions;
	private readonly IOptions<DomainRedirectOptions> DomainRedirectOptions;

	/// <summary>
	/// Initializes a new instance of the <see cref="ArticleDomainRedirectTests"/> class.
	/// </summary>
	public ArticleDomainRedirectTests()
	{
		Connection = new SqliteConnection("DataSource=:memory:");
		Connection.Open();

		DbOptions = new DbContextOptionsBuilder<WikiWikiWorldDbContext>()
			.UseSqlite(Connection)
			.Options;

		using WikiWikiWorldDbContext Context = new(DbOptions);
		Context.Database.EnsureCreated();

		MemoryCache = new MemoryCache(new MemoryCacheOptions());

		SiteConfiguration SiteConfig = new()
		{
			Sites =
			[
				new()
				{
					SiteId = 1,
					SiteName = "Magazedia",
					RootDomainIsCultureSelectorOnly = true,
					Domains = ["magazedia.com", "en.magazedia.com", "ja.magazedia.com"]
				},
				new()
				{
					SiteId = 2,
					SiteName = "Fanfare",
					DefaultCulture = "en",
					Domains = ["fanfare.wiki"]
				}
			]
		};

		SiteConfigurationOptions = Options.Create(SiteConfig);
		DomainRedirectOptions = Options.Create(new DomainRedirectOptions());
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Connection.Dispose();
		MemoryCache.Dispose();
	}

	/// <summary>
	/// Verifies that domain normalization strips schemes, ports, trailing slashes, and lowercases.
	/// </summary>
	/// <param name="RawDomain">The raw input domain.</param>
	/// <param name="Expected">The expected normalized domain.</param>
	[Theory]
	[InlineData("https://VintageComputers.ORG/", "vintagecomputers.org")]
	[InlineData("http://OLDMAG.COM:7126/some-path", "oldmag.com")]
	[InlineData("  retro-mag.wiki:443  ", "retro-mag.wiki")]
	[InlineData("sub.domain.org", "sub.domain.org")]
	public void NormalizeDomainCleansAndStandardizesDomain(string RawDomain, string Expected)
	{
		string Actual = ArticleDomainRedirectService.NormalizeDomain(RawDomain);
		Assert.Equal(Expected, Actual);
	}

	/// <summary>
	/// Verifies that a domain redirect properly survives an article rename (new revision with new slug).
	/// </summary>
	[Fact]
	public async Task DomainRedirectSurvivesArticleRename()
	{
		Guid UserId = Guid.NewGuid();
		Guid CanonicalArticleId = Guid.NewGuid();

		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			Context.Users.Add(new User
			{
				Id = UserId,
				UserName = "editor",
				DateCreated = DateTimeOffset.UtcNow
			});

			Context.ArticleRevisions.Add(new ArticleRevision
			{
				Id = 1,
				CanonicalArticleId = CanonicalArticleId,
				SiteId = 1,
				Culture = "en",
				Title = "Original Article Title",
				UrlSlug = "original-slug",
				IsCurrent = true,
				Type = ArticleType.Article,
				Text = "Original content",
				RevisionReason = "Initial create",
				CreatedByUserId = UserId,
				DateCreated = DateTimeOffset.UtcNow
			});

			await Context.SaveChangesAsync();
		}

		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			ArticleDomainRedirectService Service = new(
				Context,
				MemoryCache,
				SiteConfigurationOptions,
				DomainRedirectOptions);

			(bool Success, string? Error) = await Service.CreateRedirectAsync(
				"retro-magazines.org",
				SiteId: 1,
				Culture: "en",
				CanonicalArticleId: CanonicalArticleId,
				UserId: UserId);

			Assert.True(Success, Error);

			DefaultHttpContext HttpContext = new();
			HttpContext.Request.Scheme = "https";
			HttpContext.Request.Host = new HostString("retro-magazines.org");

			string? RedirectUrl = await Service.ResolveRedirectUrlAsync("retro-magazines.org", HttpContext.Request);
			Assert.Equal("https://en.magazedia.com/original-slug", RedirectUrl);
		}

		// Now simulate an article rename: insert a new revision with a new slug, setting IsCurrent=true
		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			ArticleRevision? OldRevision = await Context.ArticleRevisions.FindAsync(1);
			Assert.NotNull(OldRevision);
			OldRevision.IsCurrent = false;

			Context.ArticleRevisions.Add(new ArticleRevision
			{
				Id = 2,
				CanonicalArticleId = CanonicalArticleId,
				SiteId = 1,
				Culture = "en",
				Title = "Renamed Article Title",
				UrlSlug = "renamed-new-slug",
				IsCurrent = true,
				Type = ArticleType.Article,
				Text = "Updated content",
				RevisionReason = "Renamed article slug",
				CreatedByUserId = UserId,
				DateCreated = DateTimeOffset.UtcNow
			});

			await Context.SaveChangesAsync();
		}

		// Verify that without updating the redirect table, resolving the domain automatically points to the new slug!
		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			ArticleDomainRedirectService Service = new(
				Context,
				MemoryCache,
				SiteConfigurationOptions,
				DomainRedirectOptions);

			DefaultHttpContext HttpContext = new();
			HttpContext.Request.Scheme = "https";
			HttpContext.Request.Host = new HostString("retro-magazines.org");

			string? RedirectUrl = await Service.ResolveRedirectUrlAsync("retro-magazines.org", HttpContext.Request);
			Assert.Equal("https://en.magazedia.com/renamed-new-slug", RedirectUrl);
		}
	}

	/// <summary>
	/// Verifies redirect resolution for single-culture sites without subdomains and preserves query strings.
	/// </summary>
	[Fact]
	public async Task DomainRedirectForSingleCultureSitePreservesQueryString()
	{
		Guid UserId = Guid.NewGuid();
		Guid CanonicalArticleId = Guid.NewGuid();

		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			Context.Users.Add(new User
			{
				Id = UserId,
				UserName = "editor",
				DateCreated = DateTimeOffset.UtcNow
			});

			Context.ArticleRevisions.Add(new ArticleRevision
			{
				Id = 10,
				CanonicalArticleId = CanonicalArticleId,
				SiteId = 2,
				Culture = "en",
				Title = "Fanfare Article",
				UrlSlug = "fanfare-article",
				IsCurrent = true,
				Type = ArticleType.Article,
				Text = "Content",
				RevisionReason = "Initial",
				CreatedByUserId = UserId,
				DateCreated = DateTimeOffset.UtcNow
			});

			Context.ArticleDomainRedirects.Add(new ArticleDomainRedirect
			{
				Id = 1,
				Domain = "oldfanfare.org",
				SiteId = 2,
				Culture = "en",
				CanonicalArticleId = CanonicalArticleId,
				CreatedByUserId = UserId,
				DateCreated = DateTimeOffset.UtcNow
			});

			await Context.SaveChangesAsync();
		}

		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			ArticleDomainRedirectService Service = new(
				Context,
				MemoryCache,
				SiteConfigurationOptions,
				DomainRedirectOptions);

			DefaultHttpContext HttpContext = new();
			HttpContext.Request.Scheme = "https";
			HttpContext.Request.Host = new HostString("oldfanfare.org");
			HttpContext.Request.QueryString = new QueryString("?utm_source=twitter&page=1");

			string? RedirectUrl = await Service.ResolveRedirectUrlAsync("oldfanfare.org", HttpContext.Request);
			Assert.Equal("https://fanfare.wiki/fanfare-article?utm_source=twitter&page=1", RedirectUrl);
		}
	}

	/// <summary>
	/// Verifies that creating a redirect fails when domain already exists or article does not exist.
	/// </summary>
	[Fact]
	public async Task CreateRedirectValidatesDuplicatesAndNonExistentArticles()
	{
		Guid UserId = Guid.NewGuid();
		Guid CanonicalArticleId = Guid.NewGuid();

		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			Context.Users.Add(new User
			{
				Id = UserId,
				UserName = "editor",
				DateCreated = DateTimeOffset.UtcNow
			});

			Context.ArticleRevisions.Add(new ArticleRevision
			{
				Id = 1,
				CanonicalArticleId = CanonicalArticleId,
				SiteId = 1,
				Culture = "en",
				Title = "Existing Article",
				UrlSlug = "existing-article",
				IsCurrent = true,
				Type = ArticleType.Article,
				Text = "Content",
				RevisionReason = "Initial",
				CreatedByUserId = UserId,
				DateCreated = DateTimeOffset.UtcNow
			});

			await Context.SaveChangesAsync();
		}

		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			ArticleDomainRedirectService Service = new(
				Context,
				MemoryCache,
				SiteConfigurationOptions,
				DomainRedirectOptions);

			// Should fail on non-existent article
			(bool NonExistentSuccess, string? NonExistentError) = await Service.CreateRedirectAsync(
				"test-domain.org",
				SiteId: 1,
				Culture: "en",
				CanonicalArticleId: Guid.NewGuid(),
				UserId: UserId);

			Assert.False(NonExistentSuccess);
			Assert.Contains("Selected article could not be found", NonExistentError);

			// Should succeed on valid article
			(bool Success1, string? Error1) = await Service.CreateRedirectAsync(
				"test-domain.org",
				SiteId: 1,
				Culture: "en",
				CanonicalArticleId: CanonicalArticleId,
				UserId: UserId);

			Assert.True(Success1, Error1);

			// Should fail on duplicate domain
			(bool DupSuccess, string? DupError) = await Service.CreateRedirectAsync(
				"test-domain.org",
				SiteId: 1,
				Culture: "en",
				CanonicalArticleId: CanonicalArticleId,
				UserId: UserId);

			Assert.False(DupSuccess);
			Assert.Contains("already configured", DupError);
		}
	}

	/// <summary>
	/// Verifies soft-deleting a domain redirect.
	/// </summary>
	[Fact]
	public async Task DeleteRedirectSoftDeletesAndClearsCache()
	{
		Guid UserId = Guid.NewGuid();
		Guid CanonicalArticleId = Guid.NewGuid();

		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			Context.Users.Add(new User
			{
				Id = UserId,
				UserName = "editor",
				DateCreated = DateTimeOffset.UtcNow
			});

			Context.ArticleRevisions.Add(new ArticleRevision
			{
				Id = 1,
				CanonicalArticleId = CanonicalArticleId,
				SiteId = 1,
				Culture = "en",
				Title = "Article",
				UrlSlug = "article",
				IsCurrent = true,
				Type = ArticleType.Article,
				Text = "Content",
				RevisionReason = "Initial",
				CreatedByUserId = UserId,
				DateCreated = DateTimeOffset.UtcNow
			});

			Context.ArticleDomainRedirects.Add(new ArticleDomainRedirect
			{
				Id = 42,
				Domain = "to-be-deleted.org",
				SiteId = 1,
				Culture = "en",
				CanonicalArticleId = CanonicalArticleId,
				CreatedByUserId = UserId,
				DateCreated = DateTimeOffset.UtcNow
			});

			await Context.SaveChangesAsync();
		}

		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			ArticleDomainRedirectService Service = new(
				Context,
				MemoryCache,
				SiteConfigurationOptions,
				DomainRedirectOptions);

			DefaultHttpContext HttpContext = new();
			HttpContext.Request.Scheme = "https";
			HttpContext.Request.Host = new HostString("to-be-deleted.org");

			string? InitialUrl = await Service.ResolveRedirectUrlAsync("to-be-deleted.org", HttpContext.Request);
			Assert.NotNull(InitialUrl);

			bool Deleted = await Service.DeleteRedirectAsync(42);
			Assert.True(Deleted);

			string? AfterDeleteUrl = await Service.ResolveRedirectUrlAsync("to-be-deleted.org", HttpContext.Request);
			Assert.Null(AfterDeleteUrl);
		}
	}
}
