using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data;

public class WikiWikiWorldDbContext : IdentityDbContext<User, Role, Guid>
{
    public WikiWikiWorldDbContext(DbContextOptions<WikiWikiWorldDbContext> options) : base(options)
    {
    }

    public DbSet<ArticleRevision> ArticleRevisions { get; set; }
    public DbSet<FileRevision> FileRevisions { get; set; }
    public DbSet<ArticleCultureLink> ArticleCultureLinks { get; set; }
    public DbSet<DownloadUrl> DownloadUrls { get; set; }
    public DbSet<ArticleTalkSubject> ArticleTalkSubjects { get; set; }
    public DbSet<ArticleTalkSubjectPost> ArticleTalkSubjectPosts { get; set; }

    protected override void OnModelCreating(ModelBuilder Builder)
    {
        base.OnModelCreating(Builder);

        // Configure Users
        Builder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasQueryFilter(u => u.DateDeleted == null);
        });

        // Configure Roles
        Builder.Entity<Role>(b =>
        {
            b.ToTable("Roles");
            b.HasQueryFilter(r => r.DateDeleted == null);
        });

        // Configure UserRoles
        Builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>(b =>
        {
            b.ToTable("UserRoles");
        });

        // Configure UserClaims
        Builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>(b =>
        {
            b.ToTable("UserClaims");
        });

        // Configure UserLogins
        Builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>(b =>
        {
            b.ToTable("UserLogins");
        });

        // Configure RoleClaims
        Builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>(b =>
        {
            b.ToTable("RoleClaims");
        });

        // Configure UserTokens
        Builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>(b =>
        {
            b.ToTable("UserTokens");
        });

        // Configure ArticleRevision
        Builder.Entity<ArticleRevision>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasQueryFilter(e => e.DateDeleted == null);
            b.HasIndex(e => new { e.SiteId, e.Culture, e.UrlSlug, e.DateCreated });
            b.HasIndex(e => new { e.SiteId, e.Culture, e.UrlSlug, e.IsCurrent }).HasFilter("IsCurrent = 1 AND DateDeleted IS NULL");
        });

        // Configure FileRevision
        Builder.Entity<FileRevision>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasQueryFilter(e => e.DateDeleted == null);
            b.HasIndex(e => new { e.CanonicalFileId, e.DateCreated });
        });

        // Configure ArticleCultureLink
        Builder.Entity<ArticleCultureLink>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasQueryFilter(e => e.DateDeleted == null);
            b.HasIndex(e => new { e.SiteId, e.CanonicalArticleId });
        });

        // Configure DownloadUrl
        Builder.Entity<DownloadUrl>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasQueryFilter(e => e.DateDeleted == null);
            b.HasIndex(e => new { e.SiteId, e.HashSha256 });
        });

        // Configure ArticleTalkSubject
        Builder.Entity<ArticleTalkSubject>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasQueryFilter(e => e.DateDeleted == null);
            b.HasIndex(e => new { e.SiteId, e.CanonicalArticleId });
        });

        // Configure ArticleTalkSubjectPost
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
