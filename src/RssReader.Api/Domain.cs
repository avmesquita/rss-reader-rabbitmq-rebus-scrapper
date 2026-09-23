using Microsoft.EntityFrameworkCore;

namespace RssReader.Api;

public sealed class Feed
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Article
{
    public long Id { get; set; }
    public Guid FeedId { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public string? Author { get; set; }
    public string? Excerpt { get; set; }
    public string? ContentHtml { get; set; }
    public string? ContentText { get; set; }
    public string? ImageUrl { get; set; }
    public string? ImageBase64 { get; set; }
    public string? ImageMimeType { get; set; }
    public string? Category { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Feed> Feeds => Set<Feed>();
    public DbSet<Article> Articles => Set<Article>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Feed>().HasKey(feed => feed.Id);
        modelBuilder.Entity<Feed>().HasIndex(feed => feed.Url).IsUnique();
        modelBuilder.Entity<Article>().HasKey(article => article.Id);
        modelBuilder.Entity<Article>().HasIndex(article => article.CollectedAt);
        modelBuilder.Entity<Article>().HasOne<Feed>().WithMany().HasForeignKey(article => article.FeedId);
        // Articles intentionally have no unique index: repeated content must be retained.
    }
}
