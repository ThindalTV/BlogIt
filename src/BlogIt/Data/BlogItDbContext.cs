using BlogIt.Shared.Entities;
using Microsoft.EntityFrameworkCore;

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
            e.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<BlogPost>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Slug).IsUnique();
            e.HasIndex(p => p.ScheduledPublishAt);
            e.HasIndex(p => p.ScheduledUnpublishAt);
            e.Property(p => p.Title).HasMaxLength(500).IsRequired();
            e.Property(p => p.Slug).HasMaxLength(500).IsRequired();
            e.Property(p => p.Summary).IsRequired();
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
        });

        modelBuilder.Entity<MediaFile>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Title).HasMaxLength(500).IsRequired();
            e.Property(m => m.FileName).HasMaxLength(500).IsRequired();
            e.Property(m => m.ContentType).HasMaxLength(200).IsRequired();
            e.Property(m => m.BackendUrl).IsRequired();
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
            e.Property(item => item.SourcePath).HasMaxLength(1000).IsRequired();
            e.Property(item => item.TargetUrl).HasMaxLength(2000).IsRequired();
        });

        modelBuilder.Entity<SetupLock>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedNever();
        });
    }
}
