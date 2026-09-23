using Microsoft.EntityFrameworkCore;

namespace RssReader.Worker;

public sealed class WorkerDbContext(DbContextOptions<WorkerDbContext> options) : DbContext(options)
{
    public DbSet<WorkerFeed> Feeds => Set<WorkerFeed>();
    public DbSet<WorkerArticle> Articles => Set<WorkerArticle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkerFeed>().ToTable("Feeds");
        modelBuilder.Entity<WorkerFeed>().HasKey(feed => feed.Id);
        modelBuilder.Entity<WorkerArticle>().ToTable("Articles");
        modelBuilder.Entity<WorkerArticle>().HasKey(article => article.Id);
        modelBuilder.Entity<WorkerArticle>().HasIndex(article => article.CollectedAt);
    }
}

public sealed class WorkerFeed
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public bool IsActive { get; set; }
}

public sealed class WorkerArticle
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
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
}
