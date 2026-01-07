using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data;

/// <summary>
/// The database context for the WikiWikiWorld application.
/// </summary>
public class WikiWikiWorldDbContext : IdentityDbContext<User, Role, Guid>
{
	/// <summary>
	/// Filter name for soft delete filtering.
	/// </summary>
	public const string SoftDeleteFilter = nameof(SoftDeleteFilter);

	/// <summary>
	/// Filter name for multi-tenant site filtering.
	/// </summary>
	public const string SiteFilter = nameof(SiteFilter);

	/// <summary>
	/// Filter name for culture filtering.
	/// </summary>
	public const string CultureFilter = nameof(CultureFilter);

	private readonly int? _currentSiteId;
	private readonly string? _currentCulture;

	/// <summary>
	/// Initializes a new instance of the <see cref="WikiWikiWorldDbContext"/> class.
	/// </summary>
	/// <param name="Options">The options for this context.</param>
	/// <remarks>Used for migrations and design-time scenarios.</remarks>
	public WikiWikiWorldDbContext(DbContextOptions<WikiWikiWorldDbContext> Options) : base(Options)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="WikiWikiWorldDbContext"/> class with site context.
	/// </summary>
	/// <param name="Options">The options for this context.</param>
	/// <param name="SiteContext">The site context service for multi-tenant filtering.</param>
	public WikiWikiWorldDbContext(DbContextOptions<WikiWikiWorldDbContext> Options, ISiteContextService SiteContext) : base(Options)
	{
		_currentSiteId = SiteContext.GetCurrentSiteId();
		_currentCulture = SiteContext.GetCurrentCulture();
	}

	/// <summary>
	/// Gets or sets the article revisions.
	/// </summary>
	public DbSet<ArticleRevision> ArticleRevisions { get; set; } = null!;

	/// <summary>
	/// Gets or sets the file revisions.
	/// </summary>
	public DbSet<FileRevision> FileRevisions { get; set; } = null!;

	/// <summary>
	/// Gets or sets the article culture links.
	/// </summary>
	public DbSet<ArticleCultureLink> ArticleCultureLinks { get; set; } = null!;

	/// <summary>
	/// Gets or sets the download URLs.
	/// </summary>
	public DbSet<DownloadUrl> DownloadUrls { get; set; } = null!;

	/// <summary>
	/// Gets or sets the article talk subjects.
	/// </summary>
	public DbSet<ArticleTalkSubject> ArticleTalkSubjects { get; set; } = null!;

	/// <summary>
	/// Gets or sets the article talk subject posts.
	/// </summary>
	public DbSet<ArticleTalkSubjectPost> ArticleTalkSubjectPosts { get; set; } = null!;

	/// <inheritdoc/>
	protected override void OnModelCreating(ModelBuilder Builder)
	{
		base.OnModelCreating(Builder);

		Builder.Entity<User>(b =>
		{
			b.ToTable("Users");
			b.HasQueryFilter(SoftDeleteFilter, u => u.DateDeleted == null);
		});

		Builder.Entity<Role>(b =>
		{
			b.ToTable("Roles");
			b.HasQueryFilter(SoftDeleteFilter, r => r.DateDeleted == null);
		});

		Builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>(b =>
		{
			b.ToTable("UserRoles");
		});

		Builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>(b =>
		{
			b.ToTable("UserClaims");
		});

		Builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>(b =>
		{
			b.ToTable("UserLogins");
		});

		Builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>(b =>
		{
			b.ToTable("RoleClaims");
		});

		Builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>(b =>
		{
			b.ToTable("UserTokens");
		});

		Builder.Entity<ArticleRevision>(b =>
		{
			b.HasKey(e => e.Id);
			b.HasQueryFilter(SoftDeleteFilter, e => e.DateDeleted == null);
			b.HasQueryFilter(SiteFilter, e => e.SiteId == _currentSiteId);
			b.HasQueryFilter(CultureFilter, e => e.Culture == _currentCulture);
			b.HasIndex(e => new { e.SiteId, e.Culture, e.UrlSlug, e.DateCreated });
			b.HasIndex(e => new { e.SiteId, e.Culture, e.UrlSlug, e.IsCurrent }).HasFilter("IsCurrent = 1 AND DateDeleted IS NULL");
		});

		Builder.Entity<FileRevision>(b =>
		{
			b.HasKey(e => e.Id);
			b.HasQueryFilter(SoftDeleteFilter, e => e.DateDeleted == null);
			b.HasIndex(e => new { e.CanonicalFileId, e.DateCreated });
		});

		Builder.Entity<ArticleCultureLink>(b =>
		{
			b.HasKey(e => e.Id);
			b.HasQueryFilter(SoftDeleteFilter, e => e.DateDeleted == null);
			b.HasQueryFilter(SiteFilter, e => e.SiteId == _currentSiteId);
			b.HasIndex(e => new { e.SiteId, e.CanonicalArticleId });
		});

		Builder.Entity<DownloadUrl>(b =>
		{
			b.HasKey(e => e.Id);
			b.HasQueryFilter(SoftDeleteFilter, e => e.DateDeleted == null);
			b.HasQueryFilter(SiteFilter, e => e.SiteId == _currentSiteId);
			b.HasIndex(e => new { e.SiteId, e.HashSha256 });
		});

		Builder.Entity<ArticleTalkSubject>(b =>
		{
			b.HasKey(e => e.Id);
			b.HasQueryFilter(SoftDeleteFilter, e => e.DateDeleted == null);
			b.HasQueryFilter(SiteFilter, e => e.SiteId == _currentSiteId);
			b.HasIndex(e => new { e.SiteId, e.CanonicalArticleId });
		});

		Builder.Entity<ArticleTalkSubjectPost>(b =>
		{
			b.HasKey(e => e.Id);
			b.HasQueryFilter(SoftDeleteFilter, e => e.DateDeleted == null);
			b.HasIndex(e => new { e.ArticleTalkSubjectId, e.ParentTalkSubjectPostId });
		});

		// SQLite does not support DateTimeOffset natively in ORDER BY, so we convert to string
		if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
		{
			foreach (Microsoft.EntityFrameworkCore.Metadata.IEntityType EntityType in Builder.Model.GetEntityTypes())
			{
				IEnumerable<Microsoft.EntityFrameworkCore.Metadata.IMutableProperty> Properties = EntityType.GetProperties()
					.Where(p => p.ClrType == typeof(DateTimeOffset)
								|| p.ClrType == typeof(DateTimeOffset?))
					.Cast<Microsoft.EntityFrameworkCore.Metadata.IMutableProperty>();

				foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableProperty Property in Properties)
				{
					Property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToStringConverter());
				}

				IEnumerable<Microsoft.EntityFrameworkCore.Metadata.IMutableProperty> GuidProperties = EntityType.GetProperties()
					.Where(p => p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?))
					.Cast<Microsoft.EntityFrameworkCore.Metadata.IMutableProperty>();

				foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableProperty Property in GuidProperties)
				{
					Property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.GuidToStringConverter());
				}
			}
		}
	}
}
