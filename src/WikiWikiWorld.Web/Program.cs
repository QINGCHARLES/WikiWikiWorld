using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
using Microsoft.IdentityModel.Tokens;
using System.Linq;
using System.Text;
using WikiWikiWorld.Database;
using WikiWikiWorld.Data.TypeHandlers;
using WikiWikiWorld.Identity;
using WikiWikiWorld.Web;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Infrastructure;

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

// ✅ Register Database Connection Factory
ConnectionOptions ConnectionOptions = new();
DatabaseConnectionFactory ConnectionFactory = new(ConnectionOptions);

// In development, database is in workspace root's data folder
// In production, database is in the same directory as the published app
string DatabasePath = Builder.Environment.IsDevelopment()
	? Path.Combine(Builder.Environment.ContentRootPath, "..", "..", "data", "WikiWikiWorld.db")
	: Path.Combine(Builder.Environment.ContentRootPath, "WikiWikiWorld.db");

await ConnectionFactory.InitializeAsync(DatabasePath);
Builder.Services.AddSingleton<IDatabaseConnectionFactory>(ConnectionFactory);

// ✅ Register Database Repositories (Dapper-based)
Builder.Services.AddScoped<IUserRepository, UserRepository>();
Builder.Services.AddScoped<IRoleRepository, RoleRepository>();
Builder.Services.AddScoped<IUserRoleRepository, UserRoleRepository>();
Builder.Services.AddScoped<SiteResolverService>();
Builder.Services.AddTransient<ISitemapService, SitemapService>();
Builder.Services.AddHttpContextAccessor();


// ✅ Register Custom Identity Stores
Builder.Services.AddIdentity<ApplicationUser, ApplicationRole>()
	.AddUserStore<ApplicationUserStore>() // Uses Dapper User Store
	.AddRoleStore<ApplicationRoleStore>() // Uses Dapper Role Store
	.AddDefaultTokenProviders(); // Enables password reset, email confirmation tokens, etc.

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
Builder.Services.AddTransient<IArticleRevisionRepository, ArticleRevisionRepository>();
Builder.Services.AddTransient<IFileRevisionRepository, FileRevisionRepository>();
Builder.Services.AddTransient<IDownloadUrlsRepository, DownloadUrlsRepository>();

SqlMapper.AddTypeHandler<DateTimeOffset>(new DateTimeOffsetHandler());
SqlMapper.AddTypeHandler<Guid>(new GuidTypeHandler());
SqlMapper.AddTypeHandler<Guid?>(new NullableGuidTypeHandler());
SqlMapper.RemoveTypeMap(typeof(DateTimeOffset));
SqlMapper.RemoveTypeMap(typeof(Guid));
SqlMapper.RemoveTypeMap(typeof(Guid?));

FileExtensionContentTypeProvider ContentTypeProvider = new ();
ContentTypeProvider.Mappings[".heic"] = "image/heic";
ContentTypeProvider.Mappings[".avif"] = "image/avif";
//Builder.Services.AddSingleton<IContentTypeProvider>(ContentTypeProvider);

WebApplication App = Builder.Build();

App.UseAuthentication(); // Enables Identity Authentication
App.UseAuthorization(); // Enables Identity Authorization

// Configure the HTTP request pipeline.
if (!App.Environment.IsDevelopment())
{
	App.UseExceptionHandler("/Error");
	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
	App.UseHsts();
}

App.UseMiddleware<HtmlPrettifyMiddleware>();

// Inject IWebHostEnvironment to access the content root
IWebHostEnvironment Environment = App.Services.GetRequiredService<IWebHostEnvironment>();

App.MapControllers();

// In Program.cs
App.MapGet("/sitemap.xml", async (ISitemapService SitemapService) =>
{
	string Sitemap = await SitemapService.GenerateSitemapAsync();
	return Results.Content(Sitemap, "application/xml");
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

// how to add api endpoints in a class: https://codewithmukesh.com/blog/aspnet-core-webapi-crud-with-entity-framework-core-full-course

App.UseHttpsRedirection();


App.UseRouting();

// Add headers to prevent this site from being embedded in iframes.
// We attach them in OnStarting to ensure headers are set just before the response is sent.
App.Use(async (Context, Next) =>
{
	Context.Response.OnStarting(() =>
	{
		Context.Response.Headers[HeaderNames.ContentSecurityPolicy] = "frame-ancestors 'none'"; // Modern browsers: Content Security Policy frame-ancestors
		Context.Response.Headers[HeaderNames.XFrameOptions] = "DENY"; // Legacy fallback
		return Task.CompletedTask;
	});

	await Next();
});

App.UseAuthorization();

//var Provider = new FileExtensionContentTypeProvider();
//Provider.Mappings[".avif"] = "image/avif";
//Provider.Mappings[".heic"] = "image/heic";

//// Map static assets with custom MIME types
//App.MapStaticAssets(options =>
//{
//	options.ContentTypeProvider = Provider;
//});

// App.UseResponseCompression();

App.UseStaticFiles(new StaticFileOptions
{
	ContentTypeProvider = ContentTypeProvider
});



App.MapRazorPages();
   //.WithStaticAssets();

App.Run();
