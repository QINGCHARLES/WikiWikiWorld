using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography.X509Certificates;
using WikiWikiWorld.Web;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Infrastructure;
using SixLabors.ImageSharp.Web.DependencyInjection;

if (args.Length > 0 && args[0] == "backfill-categories")
{
	Environment.ExitCode = await CategoryMembershipBackfillCommand.RunAsync(args[1..]);
	return;
}

WebApplicationBuilder Builder = WebApplication.CreateBuilder(args);

Builder.Host.UseSystemd();

Builder.Services.AddOptions<ApplicationStartupOptions>()
	.Bind(Builder.Configuration.GetSection(ApplicationStartupOptions.SectionName))
	.ValidateDataAnnotations()
	.Validate(Options => Options.DevelopmentHttpsPort != Options.DevelopmentHttpPort, "Development HTTP and HTTPS ports must be different.")
	.ValidateOnStart();

ApplicationStartupOptions StartupOptions = Builder.Configuration
	.GetSection(ApplicationStartupOptions.SectionName)
	.Get<ApplicationStartupOptions>() ?? new();

// Only configure Kestrel manually for development
// In production, use appsettings.Production.json configuration
if (Builder.Environment.IsDevelopment())
{
	Builder.WebHost.ConfigureKestrel(Options =>
	{
		Options.ListenAnyIP(StartupOptions.DevelopmentHttpsPort, ListenOptions =>
		{
			// Use the development certificate for subdomain support
			X509Certificate2Collection Certs = [];

			if (!string.IsNullOrWhiteSpace(StartupOptions.DevelopmentHttpsCertificateSubjectName))
			{
				using X509Store CertStore = new(StoreName.My, StoreLocation.CurrentUser);
				CertStore.Open(OpenFlags.ReadOnly);

				Certs = CertStore.Certificates.Find(
					X509FindType.FindBySubjectName,
					StartupOptions.DevelopmentHttpsCertificateSubjectName,
					validOnly: false);
			}

			if (Certs.Count > 0)
			{
				ListenOptions.UseHttps(Certs[0]);
			}
			else
			{
				ListenOptions.UseHttps(); // Fall back to default cert
			}
		});
		Options.ListenAnyIP(StartupOptions.DevelopmentHttpPort);
	});
}

Builder.Services.Configure<SiteConfiguration>(Builder.Configuration.GetSection("SiteConfiguration"));
Builder.Services.AddSingleton(new SiteStylesheetCatalog(
	Builder.Environment.WebRootFileProvider,
	(Builder.Configuration.GetSection("SiteConfiguration").Get<SiteConfiguration>() ?? new()).Sites.Select(Site => Site.SiteId)));
Builder.Services.Configure<DomainRedirectOptions>(Builder.Configuration.GetSection(DomainRedirectOptions.SectionName));

// Add memory cache service
Builder.Services.AddMemoryCache();

// Configured Static File Path
string DataPath = string.IsNullOrWhiteSpace(StartupOptions.DataPath)
	? Path.Combine(Builder.Environment.ContentRootPath, "data")
	: Path.IsPathRooted(StartupOptions.DataPath)
		? StartupOptions.DataPath
		: Path.GetFullPath(Path.Combine(Builder.Environment.ContentRootPath, StartupOptions.DataPath));

string SiteFilesPath = Path.Combine(DataPath, "sitefiles");
string WellKnownPath = Path.Combine(DataPath, ".well-known");
string DataProtectionKeysPath = Path.Combine(DataPath, "dataprotection-keys");

// Data Protection keys encrypt Identity authentication cookies and antiforgery tokens. Persist
// them under the data volume rather than the container filesystem: update.sh replaces the
// container on every deploy, and regenerated keys would sign out every logged-in user and reject
// the form post of anyone mid-edit with a 400. SetApplicationName pins the isolation key, which
// otherwise derives from the content root path.
Directory.CreateDirectory(DataProtectionKeysPath);

Builder.Services.AddDataProtection()
	.PersistKeysToFileSystem(new DirectoryInfo(DataProtectionKeysPath))
	.SetApplicationName("WikiWikiWorld");

// Register Options
Builder.Services.AddOptions<FileStorageOptions>()
	.Bind(Builder.Configuration.GetSection("FileStorage"))
	.Configure(Options =>
	{
		Options.SiteFilesPath = SiteFilesPath;
	})
	.ValidateDataAnnotations()
	.Validate(Options => !string.IsNullOrWhiteSpace(Options.SiteFilesPath), "Site files path is required.")
	.ValidateOnStart();

string? ConnectionString = Builder.Configuration.GetConnectionString("DefaultConnection");

// FALLBACK: If no config found, calculate the default path (Local Dev or Default Prod)
if (string.IsNullOrWhiteSpace(ConnectionString))
{
    ConnectionString = $"Data Source={Path.Combine(DataPath, "app.db")}";
}

// Initialize SQLite database-level settings (WAL mode, page size)
SqliteInitializer.Initialize(ConnectionString);

// Create interceptors for per-connection PRAGMAs and durability
SqlitePragmaInterceptor PragmaInterceptor = new(
    BusyTimeoutMs: 10_000,
    CacheSizePages: -20_000,
    MmapSizeBytes: 2_147_483_648
);
SqliteDurabilityInterceptor DurabilityInterceptor = new();

Builder.Services.AddSingleton<CategoryMembershipSaveInterceptor>();
Builder.Services.AddDbContext<WikiWikiWorldDbContext>((Services, Options) =>
    Options.UseSqlite(ConnectionString, SqliteOptions =>
    {
        SqliteOptions.ExecutionStrategy(Dependencies =>
            new SqliteRetryExecutionStrategy(Dependencies, MaxRetryCount: 3));
    })
    .AddInterceptors(PragmaInterceptor, DurabilityInterceptor, Services.GetRequiredService<CategoryMembershipSaveInterceptor>()));

// ✅ Register Repositories

Builder.Services.AddScoped<SiteResolverService>();
Builder.Services.AddScoped<ISiteContextService, SiteContextService>();
Builder.Services.AddScoped<ArticleRevisionService>();
Builder.Services.AddScoped<BookmarkService>();
Builder.Services.AddScoped<ProfileService>();
Builder.Services.AddScoped<FirehoseService>();
Builder.Services.AddScoped<ArticleSearchService>();
Builder.Services.AddScoped<IArticleRenderer, ArticleRenderer>();
Builder.Services.AddScoped<FileUploadStorageService>();
Builder.Services.AddScoped<FileMigrationService>();
Builder.Services.AddScoped<ArticleDomainRedirectService>();
Builder.Services.AddScoped<ArticleLanguageService>();
Builder.Services.AddSingleton<IMarkdownPipelineFactory, MarkdownPipelineFactory>();
Builder.Services.AddTransient<ISitemapService, SitemapService>();
Builder.Services.AddHttpContextAccessor();

// ✅ Register Identity with EF Core
Builder.Services.AddIdentity<User, Role>()
    .AddEntityFrameworkStores<WikiWikiWorldDbContext>()
    .AddDefaultTokenProviders();

// ✅ Configure Identity Options (Modify as needed)
Builder.Services.Configure<IdentityOptions>(options =>
{
    // Password settings
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;

    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedAccount = false;
});

// Preserve Identity's security-stamp validation while refusing test-account cookies outside Development.
Builder.Services.ConfigureApplicationCookie(Options =>
{
    Options.Events.OnValidatePrincipal = async Context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(Context);
        if (DevelopmentAccountPolicy.IsBlocked(Builder.Environment.EnvironmentName, Context.Principal?.Identity?.Name))
        {
            Context.RejectPrincipal();
            await Context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    };
});

// ✅ Add Authentication & Authorization Middleware with JWT and Cookies
Builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "SmartScheme";
})
.AddPolicyScheme("SmartScheme", "JWT or Cookies", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        // Route Bearer requests to JWT; everything else (incl. browser cookie auth) to the
        // cookie scheme. The scheme token is matched ordinal/case-insensitively per RFC 7235
        // §2.1 (a client may legitimately send "bearer <token>"). A present-but-invalid Bearer
        // token deliberately does NOT fall back to cookies — API clients should get a 401 and
        // refresh rather than be silently cookie-authed.
        string? AuthHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(AuthHeader) && AuthHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }
        return IdentityConstants.ApplicationScheme;
    };
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    string? JwtSecret = Builder.Configuration["JwtSettings:Secret"];
    if (string.IsNullOrWhiteSpace(JwtSecret))
    {
        throw new InvalidOperationException("JWT secret is not configured. Please set the JwtSettings:Secret value in your configuration.");
    }

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = Builder.Configuration["JwtSettings:Issuer"],
        ValidAudience = Builder.Configuration["JwtSettings:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret))
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = Context =>
        {
            if (DevelopmentAccountPolicy.IsBlocked(Builder.Environment.EnvironmentName, Context.Principal?.Identity?.Name))
            {
                Context.Fail("Invalid credentials.");
            }
            return Task.CompletedTask;
        }
    };
});

Builder.Services.AddResponseCompression(options =>
{
    // Enable compression even for HTTPS requests
    options.EnableForHttps = true;
});

// 308 matches the previous rewrite-rule behavior (default is 307)
Builder.Services.AddHttpsRedirection(options =>
{
    options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
});

Builder.Services.AddAuthorization();

// Add services to the container.
Builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new ThemePageFilter());

    // Vanity URL routes (previously IIS-style rewrite rules)
    options.Conventions.AddPageRoute("/Account/Login", "login:");
    options.Conventions.AddPageRoute("/Account/Register", "register:");
    options.Conventions.AddPageRoute("/Dmca", "dmca:");
    options.Conventions.AddPageRoute("/Privacy", "privacy-policy:");
    options.Conventions.AddPageRoute("/Terms", "terms:");
    options.Conventions.AddPageRoute("/Settings/Theme", "settings/theme:");
    options.Conventions.AddPageRoute("/Settings/Theme", "@{Username}/settings");
    options.Conventions.AddPageRoute("/Article/Firehose", "firehose:");
    options.Conventions.AddPageRoute("/Search", "search:");
    // Note: "create:" is routed by ArticleRouteTransformer, not a page route convention.
    // A page targeted by the dynamic transformer must keep a single conventional route,
    // otherwise dynamic matches become ambiguous (CreateEdit is also the /edit target).
    options.Conventions.AddPageRoute("/Wiki/Upload", "wiki:upload");
    options.Conventions.AddPageRoute("/Profile/View", "@{Username}/profile");
    options.Conventions.AddPageRoute("/Profile/Contributions", "@{Username}/contributions");
    options.Conventions.AddPageRoute("/Profile/Messages", "@{Username}/messages/{Tab?}");
    options.Conventions.AddPageRoute("/Profile/Bookmarks", "@{Username}/bookmarks/{listid:int?}");
    options.Conventions.AddPageRoute("/Profile/NewMessage", "message/new");
    options.Conventions.AddPageRoute("/Profile/NewMessage", "@{Recipient}/message/new");
});

// Catch-all article routing (article view, /edit, /history, /talk, /revision, /links, /languages, 404:)
Builder.Services.AddSingleton<ArticleRouteTransformer>();
Builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

Builder.Services.AddOpenApi();

Builder.Services.AddImageSharp();

FileExtensionContentTypeProvider ContentTypeProvider = new();
ContentTypeProvider.Mappings[".heic"] = "image/heic";
ContentTypeProvider.Mappings[".avif"] = "image/avif";

WebApplication App = Builder.Build();

App.UseAuthentication(); // Enables Identity Authentication

// Configure the HTTP request pipeline.
if (!App.Environment.IsDevelopment())
{
    App.UseExceptionHandler("/Error");
    App.UseHsts();
}

App.UseStatusCodePagesWithReExecute("/NotFound", "?statusCode={0}");

using (IServiceScope StartupScope = App.Services.CreateScope())
{
	FileUploadStorageService FileUploadStorageService = StartupScope.ServiceProvider.GetRequiredService<FileUploadStorageService>();
	await FileUploadStorageService.FinalizeCommittedUploadsAndDeleteStaleTempFilesAsync(CancellationToken.None);
}

App.MapControllers();
App.MapOpenApi();
App.MapScalarApiReference("/wiki:api-docs");

Func<ISitemapService, Task<IResult>> SitemapHandler = async (ISitemapService SitemapService) =>
{
    string Sitemap = await SitemapService.GenerateSitemapAsync();
    return Results.Content(Sitemap, "application/xml");
};

App.MapGet("/sitemap.xml", SitemapHandler).ExcludeFromDescription();
App.MapGet("/info:sitemap", SitemapHandler).ExcludeFromDescription();

App.MapGet("/robots.txt", (SiteResolverService SiteResolver, HttpContext Context) =>
{
    string BaseUrl = $"{Context.Request.Scheme}://{Context.Request.Host}";

    try
    {
        // Get site context with culture-selector root domain check
        (_, _, bool IsCultureSelectorRootDomain) = SiteResolver.ResolveSiteAndCultureWithRootCheck();

        if (IsCultureSelectorRootDomain)
        {
            // Culture-selector root domain (e.g., magazedia.com) - minimal robots.txt
            // Full content is served from culture subdomains
            string MinimalContent = $"""
                User-agent: *
                Allow: /

                Sitemap: {BaseUrl}/sitemap.xml
                """;

            return Results.Content(MinimalContent, "text/plain");
        }

        // Culture subdomain or single-culture site - full robots.txt with disallows
        string Content = $"""
            User-agent: *
            Allow: /

            Disallow: /Account/
            Disallow: /Settings/
            Disallow: /Profile/Messages
            Disallow: /Article/CreateEdit
            Disallow: /Article/Delete
            Disallow: /api/
            Disallow: /openapi/
            Disallow: /wiki:api-docs/

            Sitemap: {BaseUrl}/sitemap.xml
            """;

        return Results.Content(Content, "text/plain");
    }
    catch (InvalidOperationException)
    {
        // No valid site/culture could be resolved - return minimal robots.txt
        string Content = $"""
            User-agent: *
            Allow: /

            Sitemap: {BaseUrl}/sitemap.xml
            """;

        return Results.Content(Content, "text/plain");
    }
}).ExcludeFromDescription();

// Serve .well-known BEFORE UseHttpsRedirection so certbot HTTP-01 challenges on port 80
// are served directly without being caught by the HTTP→HTTPS 308 redirect
if (!Directory.Exists(WellKnownPath))
{
	Directory.CreateDirectory(WellKnownPath);
}

App.UseStaticFiles(new StaticFileOptions
{
	FileProvider = new PhysicalFileProvider(WellKnownPath),
	RequestPath = "/.well-known",
	ServeUnknownFileTypes = true
});

App.UseHttpsRedirection();

// Canonical host redirects (www strip, domain mappings, unknown hosts) and the
// culture-selector root-domain rewrite. Config-driven via DomainRedirects + SiteConfiguration.
App.UseMiddleware<CanonicalDomainMiddleware>();

App.UseRouting();

// Add headers to prevent this site from being embedded in iframes.
App.Use(async (Context, Next) =>
{
    Context.Response.OnStarting(() =>
    {
        Context.Response.Headers[HeaderNames.ContentSecurityPolicy] = "frame-ancestors 'none'; frame-src https://www.youtube-nocookie.com";
        Context.Response.Headers[HeaderNames.XFrameOptions] = "DENY";
        return Task.CompletedTask;
    });

    await Next();
});

App.UseAuthorization();

App.UseImageSharp();

App.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = ContentTypeProvider,
    OnPrepareResponse = FileContext =>
    {
        FileContext.Context.Response.Headers.Append(HeaderNames.CacheControl, "public, max-age=2592000");
    }
});

if (!Directory.Exists(SiteFilesPath))
{
    Directory.CreateDirectory(SiteFilesPath);
}

App.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(SiteFilesPath),
    RequestPath = "/sitefiles",
    ContentTypeProvider = ContentTypeProvider,
    OnPrepareResponse = FileContext =>
    {
        FileContext.Context.Response.Headers.Append(HeaderNames.CacheControl, "public, max-age=2592000");
    }
});



App.UseMiddleware<HtmlPrettifyMiddleware>();

App.MapRazorPages();

// Lowest-precedence catch-all: routes unmatched paths into the article page space
// (view, /edit, /history, /talk, /revision, /links, /languages, 404:). Replaces the old XML catch-all
// rewrite rule; all conventional routes above win automatically.
App.MapDynamicPageRoute<ArticleRouteTransformer>($"{{**{ArticleRouteTransformer.SlugRouteValueName}}}");

App.Run();
