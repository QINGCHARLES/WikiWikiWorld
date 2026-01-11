using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Text;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Infrastructure;
using WikiWikiWorld.Web.Services;

WebApplicationBuilder Builder = WebApplication.CreateBuilder(args);

Builder.Host.UseSystemd();

// Only configure Kestrel manually for development
// In production, use appsettings.Production.json configuration
if (Builder.Environment.IsDevelopment())
{
    Builder.WebHost.ConfigureKestrel(Options =>
    {
        Options.ListenAnyIP(7126, ListenOptions =>
        {
            // Use the development certificate for subdomain support
            using System.Security.Cryptography.X509Certificates.X509Store CertStore = new(
                System.Security.Cryptography.X509Certificates.StoreName.My,
                System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
            CertStore.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);

            System.Security.Cryptography.X509Certificates.X509Certificate2Collection Certs = CertStore.Certificates.Find(
                System.Security.Cryptography.X509Certificates.X509FindType.FindBySubjectName,
                "en.localhost",
                validOnly: false);

            if (Certs.Count > 0)
            {
                ListenOptions.UseHttps(Certs[0]);
            }
            else
            {
                ListenOptions.UseHttps(); // Fall back to default cert
            }
        });
        Options.ListenAnyIP(5097); // ✅ HTTP
    });
}

Builder.Services.Configure<SiteConfiguration>(Builder.Configuration.GetSection("SiteConfiguration"));

// Add memory cache service
Builder.Services.AddMemoryCache();

// Add memory cache service
Builder.Services.AddMemoryCache();

// Configured Static File Path
string DataPath = Builder.Environment.IsDevelopment()
    ? Path.Combine(Builder.Environment.ContentRootPath, "..", "..", "data")
    : Path.Combine(Builder.Environment.ContentRootPath, "data");

string SiteFilesPath = Path.Combine(DataPath, "sitefiles");

// Register Options
Builder.Services.Configure<FileStorageOptions>(options =>
{
    options.SiteFilesPath = SiteFilesPath;
});
string? ConnectionString = Builder.Configuration.GetConnectionString("DefaultConnection");

// FALLBACK: If no config found, calculate the default path (Local Dev or Default Prod)
if (string.IsNullOrWhiteSpace(ConnectionString))
{
    ConnectionString = $"Data Source={Path.Combine(DataPath, "WikiWikiWorld.db")}";
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

Builder.Services.AddDbContext<WikiWikiWorldDbContext>(Options =>
    Options.UseSqlite(ConnectionString, SqliteOptions =>
    {
        SqliteOptions.ExecutionStrategy(Dependencies =>
            new SqliteRetryExecutionStrategy(Dependencies, MaxRetryCount: 3));
    })
    .AddInterceptors(PragmaInterceptor, DurabilityInterceptor));

// ✅ Register Repositories

Builder.Services.AddScoped<SiteResolverService>();
Builder.Services.AddScoped<ISiteContextService, SiteContextService>();
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
    options.Password.RequireNonAlphanumeric = false;
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

// ✅ Add Authentication & Authorization Middleware with JWT and Cookies
Builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "SmartScheme";
})
.AddPolicyScheme("SmartScheme", "JWT or Cookies", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        string? AuthHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(AuthHeader) && AuthHeader.StartsWith("Bearer "))
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
});

Builder.Services.AddResponseCompression(options =>
{
    // Enable compression even for HTTPS requests
    options.EnableForHttps = true;
});

Builder.Services.AddAuthorization();

// Add services to the container.
Builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new ThemePageFilter());
});
Builder.Services.AddControllers();

FileExtensionContentTypeProvider ContentTypeProvider = new();
ContentTypeProvider.Mappings[".heic"] = "image/heic";
ContentTypeProvider.Mappings[".avif"] = "image/avif";

WebApplication App = Builder.Build();

// Log CPU hardware acceleration capabilities
ILogger StartupLogger = App.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
if (Avx512F.IsSupported)
{
    StartupLogger.LogInformation("🚀 Hardware Acceleration: AVX-512 (x86-64-v4) is ACTIVE");
}
else if (Avx2.IsSupported)
{
    StartupLogger.LogInformation("✅ Hardware Acceleration: AVX2 (x86-64-v3) is ACTIVE");
}
else if (AdvSimd.IsSupported)
{
    StartupLogger.LogInformation("✅ Hardware Acceleration: ARM NEON (AdvSimd) is ACTIVE");
}
else
{
    StartupLogger.LogWarning("⚠️ Hardware Acceleration: Standard instruction set (x86-64-v1/v2)");
}

App.UseAuthentication(); // Enables Identity Authentication

// Configure the HTTP request pipeline.
if (!App.Environment.IsDevelopment())
{
    App.UseExceptionHandler("/Error");
    App.UseHsts();
}

App.UseStatusCodePagesWithReExecute("/NotFound", "?statusCode={0}");



// Inject IWebHostEnvironment to access the content root
IWebHostEnvironment Environment = App.Services.GetRequiredService<IWebHostEnvironment>();

App.MapControllers();

App.MapGet("/sitemap.xml", async (ISitemapService SitemapService) =>
{
    string Sitemap = await SitemapService.GenerateSitemapAsync();
    return Results.Content(Sitemap, "application/xml");
});

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
});

// Load the XML file from the correct path
string RewriteFile = Path.Combine(Environment.ContentRootPath, "Config", "UrlRewriteRules.xml");
if (File.Exists(RewriteFile))
{
    using StreamReader StreamReader = File.OpenText(RewriteFile);
    RewriteOptions Options = new RewriteOptions().AddIISUrlRewrite(StreamReader);
    App.UseRewriter(Options);
}
else
{
    throw new FileNotFoundException("Rewrite rules file not found", RewriteFile);
}

App.UseHttpsRedirection();

App.UseRouting();

// Add headers to prevent this site from being embedded in iframes.
App.Use(async (Context, Next) =>
{
    Context.Response.OnStarting(() =>
    {
        Context.Response.Headers[HeaderNames.ContentSecurityPolicy] = "frame-ancestors 'none'";
        Context.Response.Headers[HeaderNames.XFrameOptions] = "DENY";
        return Task.CompletedTask;
    });

    await Next();
});

App.UseAuthorization();

App.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = ContentTypeProvider
});

if (!Directory.Exists(SiteFilesPath))
{
    Directory.CreateDirectory(SiteFilesPath);
}

App.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(SiteFilesPath),
    RequestPath = "/sitefiles",
    ContentTypeProvider = ContentTypeProvider
});

App.UseMiddleware<HtmlPrettifyMiddleware>();

App.MapRazorPages();

App.Run();
