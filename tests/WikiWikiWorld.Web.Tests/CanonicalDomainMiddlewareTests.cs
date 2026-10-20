using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Infrastructure;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies that <see cref="CanonicalDomainMiddleware"/> redirects hosts that are not configured sites
/// without resolving the request-scoped, site-aware database context.
/// </summary>
public sealed class CanonicalDomainMiddlewareTests : IDisposable
{
	private readonly SqliteConnection Connection;
	private readonly SiteConfiguration SiteConfig;
	private readonly DomainRedirectOptions RedirectOptions;

	/// <summary>
	/// Initializes a new instance of the <see cref="CanonicalDomainMiddlewareTests"/> class with the
	/// production-shaped site and redirect configuration and an in-memory SQLite database.
	/// </summary>
	public CanonicalDomainMiddlewareTests()
	{
		Connection = new SqliteConnection("DataSource=:memory:");
		Connection.Open();

		SiteConfig = new()
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

		RedirectOptions = new()
		{
			StripWww = true,
			HostMappings = new(StringComparer.OrdinalIgnoreCase) { ["magazedia.wiki"] = "magazedia.com" },
			UnknownHostTarget = "en.magazedia.com"
		};
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Connection.Dispose();
	}

	/// <summary>
	/// Verifies that a mapped domain, including a culture subdomain of it, gets a permanent redirect to
	/// the mapped host with path and query preserved, using a service provider with no registrations so
	/// any attempt to reach the database would throw.
	/// </summary>
	/// <param name="Host">The incoming request host.</param>
	/// <param name="ExpectedLocation">The expected redirect location.</param>
	[Theory]
	[InlineData("magazedia.wiki", "https://magazedia.com/some-article?page=2")]
	[InlineData("en.magazedia.wiki", "https://en.magazedia.com/some-article?page=2")]
	public async Task MappedHostRedirectsPermanentlyWithoutDatabase(string Host, string ExpectedLocation)
	{
		await using ServiceProvider EmptyServices = new ServiceCollection().BuildServiceProvider();
		DefaultHttpContext HttpContext = CreateHttpContext(Host, EmptyServices);
		bool NextCalled = false;

		CanonicalDomainMiddleware Middleware = CreateMiddleware(_ =>
		{
			NextCalled = true;
			return Task.CompletedTask;
		});

		await Middleware.InvokeAsync(HttpContext);

		Assert.Equal(StatusCodes.Status301MovedPermanently, HttpContext.Response.StatusCode);
		Assert.Equal(ExpectedLocation, HttpContext.Response.Headers.Location.ToString());
		Assert.False(NextCalled);
	}

	/// <summary>
	/// Verifies, through the production DI chain (site-aware context, site resolver, HTTP context
	/// accessor), that an article redirect domain and an unknown host are redirected rather than failing
	/// because the host is not a configured site.
	/// </summary>
	[Fact]
	public async Task UnconfiguredHostsRedirectThroughProductionServiceChain()
	{
		Guid UserId = Guid.NewGuid();
		Guid CanonicalArticleId = Guid.NewGuid();

		DbContextOptions<WikiWikiWorldDbContext> SeedOptions = new DbContextOptionsBuilder<WikiWikiWorldDbContext>()
			.UseSqlite(Connection)
			.Options;

		await using (WikiWikiWorldDbContext SeedContext = new(SeedOptions))
		{
			SeedContext.Database.EnsureCreated();

			SeedContext.Users.Add(new User
			{
				Id = UserId,
				UserName = "editor",
				DateCreated = DateTimeOffset.UtcNow
			});

			SeedContext.ArticleRevisions.Add(new ArticleRevision
			{
				Id = 1,
				CanonicalArticleId = CanonicalArticleId,
				SiteId = 1,
				Culture = "en",
				Title = "Retro Magazines",
				UrlSlug = "retro-magazines",
				IsCurrent = true,
				Type = ArticleType.Article,
				Text = "Content",
				RevisionReason = "Initial",
				CreatedByUserId = UserId,
				DateCreated = DateTimeOffset.UtcNow
			});

			SeedContext.ArticleDomainRedirects.Add(new ArticleDomainRedirect
			{
				Id = 1,
				Domain = "retro-magazines.org",
				SiteId = 1,
				Culture = "en",
				CanonicalArticleId = CanonicalArticleId,
				CreatedByUserId = UserId,
				DateCreated = DateTimeOffset.UtcNow
			});

			await SeedContext.SaveChangesAsync();
		}

		ServiceCollection Services = new();
		Services.AddHttpContextAccessor();
		Services.AddMemoryCache();
		Services.AddSingleton(Options.Create(SiteConfig));
		Services.AddSingleton(Options.Create(RedirectOptions));
		Services.AddScoped<SiteResolverService>();
		Services.AddScoped<ISiteContextService, SiteContextService>();
		Services.AddDbContext<WikiWikiWorldDbContext>(Builder => Builder.UseSqlite(Connection));
		await using ServiceProvider RootServices = Services.BuildServiceProvider();

		(string Host, int ExpectedStatus, string ExpectedLocation)[] Cases =
		[
			("retro-magazines.org", StatusCodes.Status308PermanentRedirect, "https://en.magazedia.com/retro-magazines?page=2"),
			("unknown.example", StatusCodes.Status308PermanentRedirect, "https://en.magazedia.com/some-article?page=2")
		];

		foreach ((string Host, int ExpectedStatus, string ExpectedLocation) in Cases)
		{
			await using AsyncServiceScope Scope = RootServices.CreateAsyncScope();
			DefaultHttpContext HttpContext = CreateHttpContext(Host, Scope.ServiceProvider);
			Scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = HttpContext;

			// Precondition: the request-scoped context really does fail for this host, as in production.
			Assert.Throws<InvalidOperationException>(() =>
			{
				using IServiceScope ProbeScope = RootServices.CreateScope();
				ProbeScope.ServiceProvider.GetRequiredService<WikiWikiWorldDbContext>();
			});

			bool NextCalled = false;
			CanonicalDomainMiddleware Middleware = CreateMiddleware(_ =>
			{
				NextCalled = true;
				return Task.CompletedTask;
			});

			await Middleware.InvokeAsync(HttpContext);

			Assert.Equal(ExpectedStatus, HttpContext.Response.StatusCode);
			Assert.Equal(ExpectedLocation, HttpContext.Response.Headers.Location.ToString());
			Assert.False(NextCalled);
		}
	}

	/// <summary>
	/// Builds an HTTPS request for <c>/some-article?page=2</c> on the given host.
	/// </summary>
	/// <param name="Host">The request host.</param>
	/// <param name="RequestServices">The request service provider the middleware resolves from.</param>
	/// <returns>The configured HTTP context.</returns>
	private static DefaultHttpContext CreateHttpContext(string Host, IServiceProvider RequestServices)
	{
		DefaultHttpContext HttpContext = new() { RequestServices = RequestServices };
		HttpContext.Request.Scheme = "https";
		HttpContext.Request.Host = new HostString(Host);
		HttpContext.Request.Path = "/some-article";
		HttpContext.Request.QueryString = new QueryString("?page=2");
		return HttpContext;
	}

	/// <summary>
	/// Creates the middleware under test with this fixture's redirect and site configuration.
	/// </summary>
	/// <param name="Next">The next delegate, used to detect whether the request was passed on.</param>
	/// <returns>The middleware instance.</returns>
	private CanonicalDomainMiddleware CreateMiddleware(RequestDelegate Next)
	{
		return new CanonicalDomainMiddleware(Next, Options.Create(RedirectOptions), Options.Create(SiteConfig));
	}
}
