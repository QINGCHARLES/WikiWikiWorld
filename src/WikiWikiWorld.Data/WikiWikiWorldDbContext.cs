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
    /// Initializes a new instance of the <see cref="WikiWikiWorldDbContext"/> class.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    public WikiWikiWorldDbContext(DbContextOptions<WikiWikiWorldDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Gets or sets the article revisions.
    /// </summary>
    public DbSet<ArticleRevision> ArticleRevisions { get; set; }

    /// <summary>
    /// Gets or sets the file revisions.
    /// </summary>
    public DbSet<FileRevision> FileRevisions { get; set; }

    /// <summary>
    /// Gets or sets the article culture links.
    /// </summary>
    public DbSet<ArticleCultureLink> ArticleCultureLinks { get; set; }

    /// <summary>
    /// Gets or sets the download URLs.
    /// </summary>
    public DbSet<DownloadUrl> DownloadUrls { get; set; }

    /// <summary>
    /// Gets or sets the article talk subjects.
    /// </summary>
    public DbSet<ArticleTalkSubject> ArticleTalkSubjects { get; set; }

    /// <summary>
    /// Gets or sets the article talk subject posts.
    /// </summary>
    public DbSet<ArticleTalkSubjectPost> ArticleTalkSubjectPosts { get; set; }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder Builder)
    {
        base.OnModelCreating(Builder);

        Builder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasQueryFilter(u => u.DateDeleted == null);
        });

        Builder.Entity<Role>(b =>
        {
            b.ToTable("Roles");
            b.HasQueryFilter(r => r.DateDeleted == null);
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
            b.HasQueryFilter(e => e.DateDeleted == null);
            b.HasIndex(e => new { e.SiteId, e.Culture, e.UrlSlug, e.DateCreated });
            b.HasIndex(e => new { e.SiteId, e.Culture, e.UrlSlug, e.IsCurrent }).HasFilter("IsCurrent = 1 AND DateDeleted IS NULL");
        });

        Builder.Entity<FileRevision>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasQueryFilter(e => e.DateDeleted == null);
            b.HasIndex(e => new { e.CanonicalFileId, e.DateCreated });
        });

        Builder.Entity<ArticleCultureLink>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasQueryFilter(e => e.DateDeleted == null);
            b.HasIndex(e => new { e.SiteId, e.CanonicalArticleId });
        });

        Builder.Entity<DownloadUrl>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasQueryFilter(e => e.DateDeleted == null);
            b.HasIndex(e => new { e.SiteId, e.HashSha256 });
        });

        Builder.Entity<ArticleTalkSubject>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasQueryFilter(e => e.DateDeleted == null);
            b.HasIndex(e => new { e.SiteId, e.CanonicalArticleId });
        });

        Builder.Entity<ArticleTalkSubjectPost>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasQueryFilter(e => e.DateDeleted == null);
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
