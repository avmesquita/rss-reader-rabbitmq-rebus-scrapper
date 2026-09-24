using Microsoft.EntityFrameworkCore;

namespace RssReader.Api;

public sealed class Feed
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset? NextScheduledAt { get; set; }
    public string? LastError { get; set; }
    public int LastCollectedCount { get; set; }
}

public sealed class Article
{
    public long Id { get; set; }
    public Guid FeedId { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public required string UrlHash { get; set; }
    public required string TitleHash { get; set; }
    public string? Author { get; set; }
    public string? Excerpt { get; set; }
    public string? ContentHtml { get; set; }
    public string? ContentText { get; set; }
    public string? ImageUrl { get; set; }
    public string? ImageBase64 { get; set; }
    public string? ImageMimeType { get; set; }
    public string? Category { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
}

public sealed class FeedIngestionRun
{
    public Guid Id { get; set; }
    public Guid FeedId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Status { get; set; } = "Running";
    public int ItemCount { get; set; }
    public int ProcessedCount { get; set; }
    public int PersistedCount { get; set; }
    public int ErrorCount { get; set; }
    public string? Error { get; set; }
}

public sealed class FeedIngestionError
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public Guid FeedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Stage { get; set; } = "Unknown";
    public string? ArticleUrl { get; set; }
    public required string Message { get; set; }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Feed> Feeds => Set<Feed>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<FeedIngestionRun> IngestionRuns => Set<FeedIngestionRun>();
    public DbSet<FeedIngestionError> IngestionErrors => Set<FeedIngestionError>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Feed>().HasKey(feed => feed.Id);
        modelBuilder.Entity<Feed>().HasIndex(feed => feed.Url).IsUnique();
        modelBuilder.Entity<Article>().HasKey(article => article.Id);
        modelBuilder.Entity<Article>().HasIndex(article => article.CollectedAt);
        modelBuilder.Entity<Article>().HasIndex(article => article.UrlHash).IsUnique();
        modelBuilder.Entity<Article>().HasIndex(article => article.TitleHash);
        modelBuilder.Entity<Article>().HasOne<Feed>().WithMany().HasForeignKey(article => article.FeedId);
        modelBuilder.Entity<FeedIngestionRun>().HasKey(run => run.Id);
        modelBuilder.Entity<FeedIngestionRun>().HasIndex(run => new { run.FeedId, run.StartedAt });
        modelBuilder.Entity<FeedIngestionError>().HasKey(error => error.Id);
        modelBuilder.Entity<FeedIngestionError>().HasIndex(error => new { error.FeedId, error.CreatedAt });
    }
}
