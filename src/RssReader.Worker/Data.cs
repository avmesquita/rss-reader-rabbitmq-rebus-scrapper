using Microsoft.EntityFrameworkCore;

namespace RssReader.Worker;

public sealed class WorkerDbContext(DbContextOptions<WorkerDbContext> options) : DbContext(options)
{
    public DbSet<WorkerFeed> Feeds => Set<WorkerFeed>();
    public DbSet<WorkerArticle> Articles => Set<WorkerArticle>();
    public DbSet<WorkerIngestionRun> IngestionRuns => Set<WorkerIngestionRun>();
    public DbSet<WorkerIngestionError> IngestionErrors => Set<WorkerIngestionError>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkerFeed>().ToTable("Feeds");
        modelBuilder.Entity<WorkerFeed>().HasKey(feed => feed.Id);
        modelBuilder.Entity<WorkerArticle>().ToTable("Articles");
        modelBuilder.Entity<WorkerArticle>().HasKey(article => article.Id);
        modelBuilder.Entity<WorkerArticle>().HasIndex(article => article.CollectedAt);
        modelBuilder.Entity<WorkerArticle>().HasIndex(article => article.UrlHash).IsUnique();
        modelBuilder.Entity<WorkerArticle>().HasIndex(article => article.TitleHash);
        modelBuilder.Entity<WorkerIngestionRun>().ToTable("IngestionRuns");
        modelBuilder.Entity<WorkerIngestionRun>().HasKey(run => run.Id);
        modelBuilder.Entity<WorkerIngestionError>().ToTable("IngestionErrors");
        modelBuilder.Entity<WorkerIngestionError>().HasKey(error => error.Id);
    }
}

public sealed class WorkerFeed
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset? NextScheduledAt { get; set; }
    public string? LastError { get; set; }
    public int LastCollectedCount { get; set; }
}

public sealed class WorkerArticle
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
    public bool IsHidden { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
}

public sealed class WorkerIngestionRun
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

public sealed class WorkerIngestionError
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public Guid FeedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Stage { get; set; } = "Unknown";
    public string? ArticleUrl { get; set; }
    public required string Message { get; set; }
}
