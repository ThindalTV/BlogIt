using BlogIt.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogIt.Shared.Data;

public class BlogItDbContext(DbContextOptions<BlogItDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<UrlRedirect> UrlRedirects => Set<UrlRedirect>();
    public DbSet<SetupLock> SetupLocks => Set<SetupLock>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(100).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
            // A BCrypt hash is always 60 characters; 100 leaves room for a different algorithm's
            // prefix without reaching nvarchar(max) and pushing the column off-row.
            e.Property(u => u.PasswordHash).HasMaxLength(100).IsRequired();
            e.Property(u => u.SecurityStamp).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<BlogPost>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Slug).IsUnique();
            e.HasIndex(p => p.ScheduledPublishAt);
            e.HasIndex(p => p.ScheduledUnpublishAt);
            // Covers the query behind every public listing — front page, archive, tag pages and
            // both feeds all filter on IsPublished with a non-null PublishedAt and then order by
            // PublishedAt descending. Without it each of those scans and sorts the whole table,
            // including the nvarchar(max) content column, on every uncached request. Declared
            // descending on PublishedAt so the index order matches the ORDER BY and SQL Server can
            // read it forwards.
            e.HasIndex(p => new { p.IsPublished, p.PublishedAt })
             .IsDescending(false, true);
            e.Property(p => p.Title).HasMaxLength(500).IsRequired();
            e.Property(p => p.Slug).HasMaxLength(500).IsRequired();
            e.Property(p => p.Summary).IsRequired();
            ConfigureSeoColumns(e);
            e.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
            e.HasOne(p => p.Author)
             .WithMany(u => u.BlogPosts)
             .HasForeignKey(p => p.AuthorId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(p => p.Tags)
             .WithMany(t => t.BlogPosts);
        });

        modelBuilder.Entity<Tag>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.Name).HasMaxLength(100).IsRequired();
            e.Property(t => t.Slug).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<Page>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Slug).IsUnique();
            e.HasIndex(p => p.ScheduledPublishAt);
            e.HasIndex(p => p.ScheduledUnpublishAt);
            e.Property(p => p.Title).HasMaxLength(500).IsRequired();
            e.Property(p => p.Slug).HasMaxLength(500).IsRequired();
            e.Property(p => p.Content).IsRequired();
            ConfigureSeoColumns(e);
            e.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
        });

        modelBuilder.Entity<MediaFile>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Title).HasMaxLength(500).IsRequired();
            e.Property(m => m.FileName).HasMaxLength(500).IsRequired();
            e.Property(m => m.ContentType).HasMaxLength(200).IsRequired();
            // MediaProxyApi resolves every single media request by matching this column, so it
            // has to be indexable — which nvarchar(max) is not, and which is what it was before a
            // length was given here. Unique because the storage key already is: both providers
            // generate a GUID name and refuse to overwrite. 200 is far above the ~43 characters a
            // key actually uses (32 hex plus an extension).
            e.Property(m => m.BackendUrl).HasMaxLength(200).IsRequired();
            e.HasIndex(m => m.BackendUrl).IsUnique();
            e.Property(m => m.PublicPath).HasMaxLength(1000).IsRequired();
            e.HasOne(m => m.UploadedByUser)
             .WithMany(u => u.MediaFiles)
             .HasForeignKey(m => m.UploadedByUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SiteSetting>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(200).IsRequired();
            e.Property(s => s.Value).IsRequired();
        });

        modelBuilder.Entity<AiConversation>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Title).HasMaxLength(500).IsRequired();
            e.Property(c => c.Summary).IsRequired(false);
            e.HasOne(c => c.CreatedByUser)
             .WithMany(u => u.AiConversations)
             .HasForeignKey(c => c.CreatedByUserId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.LinkedDraft)
             .WithMany()
             .HasForeignKey(c => c.LinkedDraftId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);
            e.HasMany(c => c.Messages)
             .WithOne(m => m.Conversation)
             .HasForeignKey(m => m.ConversationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiMessage>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Role).HasMaxLength(20).IsRequired();
            e.Property(m => m.Content).IsRequired();
        });

        modelBuilder.Entity<UrlRedirect>(e =>
        {
            e.HasKey(item => item.Id);
            e.HasIndex(item => item.SourcePath).IsUnique();
            // 450 characters is 900 bytes of nvarchar, inside SQL Server's 1700-byte limit for a
            // nonclustered index key. At the previous 1000 the unique index above was created with
            // only a warning and then failed at insert time for any row that actually used the
            // width — reachable through the normal API, since RedirectPathValidator allowed 1000.
            // That validator's cap now matches this number; the two must move together.
            e.Property(item => item.SourcePath).HasMaxLength(RedirectLimits.SourcePathLength).IsRequired();
            e.Property(item => item.TargetUrl).HasMaxLength(RedirectLimits.TargetUrlLength).IsRequired();
        });

        modelBuilder.Entity<SetupLock>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedNever();
        });
    }

    /// <summary>
    /// Applies the shared SEO column widths from <see cref="SeoLimits"/>, so posts and pages
    /// cannot drift apart the way their hand-copied API validation already has.
    /// </summary>
    private static void ConfigureSeoColumns<TEntity>(EntityTypeBuilder<TEntity> e)
        where TEntity : class, ISeoMetadata
    {
        e.Property(entity => entity.SeoTitle).HasMaxLength(SeoLimits.TitleLength);
        e.Property(entity => entity.SeoDescription).HasMaxLength(SeoLimits.DescriptionLength);
        e.Property(entity => entity.SeoKeywords).HasMaxLength(SeoLimits.KeywordsLength);
        e.Property(entity => entity.OgImageUrl).HasMaxLength(SeoLimits.OgImageUrlLength);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BumpConcurrencyStamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        BumpConcurrencyStamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Gives every modified <see cref="IConcurrencyStamped"/> entity a new token. EF Core keeps the
    /// original value and puts it in the update's <c>WHERE</c> clause, so a second writer working
    /// from a stale copy matches zero rows and raises
    /// <see cref="DbUpdateConcurrencyException"/> instead of silently overwriting.
    /// </summary>
    /// <remarks>
    /// Done here rather than with a SQL Server <c>rowversion</c> so it behaves identically on every
    /// provider BlogIt ships, including the in-memory one the test suite runs on — a store-generated
    /// token would simply never populate there.
    /// </remarks>
    private void BumpConcurrencyStamps()
    {
        foreach (var entry in ChangeTracker.Entries<IConcurrencyStamped>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.ConcurrencyStamp = Guid.NewGuid();
        }
    }
}
