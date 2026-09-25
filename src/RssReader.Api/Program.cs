using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using Rebus.ServiceProvider;
using RssReader.Api;
using RssReader.Contracts;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var workerQueue = configuration["RabbitMq:WorkerQueue"] ?? "rss-reader-worker";

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Postgres")));
builder.Services.AddSingleton<ApiDiagnostics>();
builder.Services.AddHttpClient<RabbitDiagnostics>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRebus((configure, _) => configure
    .Transport(transport => transport.UseRabbitMq(configuration["RabbitMq:ConnectionString"]!, "rss-reader-api"))
    .Routing(route => route.TypeBased().Map<IngestFeedCommand>(workerQueue)));

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (exception is not null)
        context.RequestServices.GetRequiredService<ApiDiagnostics>().Record(context, exception);

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await Results.Problem("Ocorreu um erro interno na API.").ExecuteAsync(context);
}));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Articles\" ADD COLUMN IF NOT EXISTS \"ImageUrl\" text;");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Articles\" ADD COLUMN IF NOT EXISTS \"ImageBase64\" text;");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Articles\" ADD COLUMN IF NOT EXISTS \"ImageMimeType\" text;");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Articles\" ADD COLUMN IF NOT EXISTS \"Category\" text;");
    await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS \"IX_Articles_UrlHash\";");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Articles\" ADD COLUMN IF NOT EXISTS \"IsFavorite\" boolean NOT NULL DEFAULT false;");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Articles\" ADD COLUMN IF NOT EXISTS \"IsHidden\" boolean NOT NULL DEFAULT false;");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Articles\" ADD COLUMN IF NOT EXISTS \"UrlHash\" text;");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Articles\" ADD COLUMN IF NOT EXISTS \"TitleHash\" text;");
    var articlesWithoutIdentity = await db.Articles
        .Where(article => article.UrlHash == null || article.TitleHash == null)
        .Select(article => new { article.Id, article.Url, article.Title })
        .ToListAsync();
    foreach (var article in articlesWithoutIdentity)
    {
        var normalizedUrl = NormalizeArticleUrl(article.Url);
        var urlHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl)));
        var titleHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(article.Title.Trim())));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Articles\" SET \"UrlHash\" = {urlHash}, \"TitleHash\" = {titleHash} WHERE \"Id\" = {article.Id};");
    }
    await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Articles\" older USING \"Articles\" newer WHERE older.\"Id\" > newer.\"Id\" AND older.\"UrlHash\" = newer.\"UrlHash\";");
    await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Articles_UrlHash\" ON \"Articles\" (\"UrlHash\") WHERE \"UrlHash\" IS NOT NULL;");
    await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_Articles_TitleHash\" ON \"Articles\" (\"TitleHash\");");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Feeds\" ADD COLUMN IF NOT EXISTS \"LastCheckedAt\" timestamptz;");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Feeds\" ADD COLUMN IF NOT EXISTS \"NextScheduledAt\" timestamptz;");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Feeds\" ADD COLUMN IF NOT EXISTS \"LastError\" text;");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Feeds\" ADD COLUMN IF NOT EXISTS \"LastCollectedCount\" integer NOT NULL DEFAULT 0;");
    await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"IngestionRuns\" (\"Id\" uuid PRIMARY KEY, \"FeedId\" uuid NOT NULL, \"StartedAt\" timestamptz NOT NULL, \"CompletedAt\" timestamptz NULL, \"Status\" text NOT NULL, \"ItemCount\" integer NOT NULL DEFAULT 0, \"PersistedCount\" integer NOT NULL DEFAULT 0, \"ErrorCount\" integer NOT NULL DEFAULT 0, \"Error\" text NULL);");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"IngestionRuns\" ADD COLUMN IF NOT EXISTS \"ProcessedCount\" integer NOT NULL DEFAULT 0;");
    await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_IngestionRuns_FeedId_StartedAt\" ON \"IngestionRuns\" (\"FeedId\", \"StartedAt\");");
    await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"IngestionErrors\" (\"Id\" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, \"RunId\" uuid NOT NULL, \"FeedId\" uuid NOT NULL, \"CreatedAt\" timestamptz NOT NULL, \"Stage\" text NOT NULL, \"ArticleUrl\" text NULL, \"Message\" text NOT NULL);");
    await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_IngestionErrors_FeedId_CreatedAt\" ON \"IngestionErrors\" (\"FeedId\", \"CreatedAt\");");
}

static string NormalizeArticleUrl(string url)
{
    if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        return url.Trim();

    var builder = new UriBuilder(uri) { Fragment = string.Empty };
    return builder.Uri.ToString().TrimEnd('/');
}

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/api/feeds", async (AppDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Feeds.AsNoTracking().OrderByDescending(feed => feed.CreatedAt).ToListAsync(cancellationToken)));

app.MapGet("/api/dashboard", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    var feeds = await db.Feeds.AsNoTracking().OrderBy(feed => feed.Name).ToListAsync(cancellationToken);
    var stats = new
    {
        totalArticles = await db.Articles.CountAsync(cancellationToken),
        visibleArticles = await db.Articles.CountAsync(article => !article.IsHidden, cancellationToken),
        hiddenArticles = await db.Articles.CountAsync(article => article.IsHidden, cancellationToken),
        favorites = await db.Articles.CountAsync(article => article.IsFavorite && !article.IsHidden, cancellationToken)
    };
    return Results.Ok(new { stats, feeds });
});

app.MapGet("/api/feeds/{id:guid}/ingestion-runs", async (Guid id, AppDbContext db, int? limit, CancellationToken cancellationToken) =>
{
    var feedExists = await db.Feeds.AnyAsync(feed => feed.Id == id, cancellationToken);
    if (!feedExists)
        return Results.NotFound();

    var take = Math.Clamp(limit ?? 20, 1, 100);
    var runs = await db.IngestionRuns.AsNoTracking()
        .Where(run => run.FeedId == id)
        .OrderByDescending(run => run.StartedAt)
        .Take(take)
        .ToListAsync(cancellationToken);
    return Results.Ok(runs);
});

app.MapGet("/api/feeds/{id:guid}/ingestion-errors", async (Guid id, AppDbContext db, int? limit, CancellationToken cancellationToken) =>
{
    var feedExists = await db.Feeds.AnyAsync(feed => feed.Id == id, cancellationToken);
    if (!feedExists)
        return Results.NotFound();

    var take = Math.Clamp(limit ?? 50, 1, 200);
    var errors = await db.IngestionErrors.AsNoTracking()
        .Where(error => error.FeedId == id)
        .OrderByDescending(error => error.CreatedAt)
        .Take(take)
        .ToListAsync(cancellationToken);
    return Results.Ok(errors);
});

app.MapGet("/api/ingestion-errors", async (AppDbContext db, int? limit, CancellationToken cancellationToken) =>
{
    var take = Math.Clamp(limit ?? 100, 1, 500);
    var errors = await (from error in db.IngestionErrors.AsNoTracking()
                        join feed in db.Feeds.AsNoTracking() on error.FeedId equals feed.Id
                        orderby error.CreatedAt descending
                        select new { error, feedName = feed.Name })
        .Take(take)
        .ToListAsync(cancellationToken);
    return Results.Ok(errors);
});

app.MapPost("/api/feeds", async (CreateFeedRequest request, AppDbContext db, IBus bus, CancellationToken cancellationToken) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        return Results.BadRequest(new { error = "Informe uma URL HTTP ou HTTPS válida." });

    var feed = new Feed { Id = Guid.NewGuid(), Name = request.Name.Trim(), Url = uri.ToString(), CreatedAt = DateTimeOffset.UtcNow };
    db.Feeds.Add(feed);
    await db.SaveChangesAsync(cancellationToken);
    await bus.Send(new IngestFeedCommand(feed.Id, feed.Url, Guid.NewGuid()));
    return Results.Created($"/api/feeds/{feed.Id}", feed);
});

app.MapPost("/api/feeds/{id:guid}/refresh", async (Guid id, AppDbContext db, IBus bus, CancellationToken cancellationToken) =>
{
    var feed = await db.Feeds.SingleOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken);
    if (feed is null)
        return Results.NotFound();

    var elapsed = feed.LastCheckedAt.HasValue ? DateTimeOffset.UtcNow - feed.LastCheckedAt.Value : TimeSpan.FromMinutes(30);
    if (elapsed < TimeSpan.FromMinutes(30))
    {
        var retryAfterSeconds = (int)Math.Ceiling((TimeSpan.FromMinutes(30) - elapsed).TotalSeconds);
        return Results.Problem(
            detail: $"A fonte poderá ser atualizada novamente em {Math.Ceiling(retryAfterSeconds / 60d):0} minutos.",
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfterSeconds });
    }

    var runId = Guid.NewGuid();
    await bus.Send(new IngestFeedCommand(feed.Id, feed.Url, runId));
    return Results.Accepted($"/api/feeds/{feed.Id}", new { message = "Atualização enviada para processamento.", runId });
});

app.MapDelete("/api/feeds/{id:guid}", async (Guid id, AppDbContext db, CancellationToken cancellationToken) =>
{
    var feed = await db.Feeds.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (feed is null)
        return Results.NotFound();

    await db.Articles.Where(article => article.FeedId == id).ExecuteDeleteAsync(cancellationToken);
    db.Feeds.Remove(feed);
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/articles", async (AppDbContext db, string? search, string? category, Guid? feedId, bool favoritesOnly, int? periodHours, string? sort, int? page, int? pageSize, CancellationToken cancellationToken) =>
{
    var query = from article in db.Articles.AsNoTracking()
                join feed in db.Feeds.AsNoTracking() on article.FeedId equals feed.Id
                select new { article, feed };

    if (!string.IsNullOrWhiteSpace(search))
    {
        var pattern = $"%{search.Trim()}%";
        query = query.Where(item =>
            EF.Functions.ILike(item.article.Title, pattern)
            || EF.Functions.ILike(item.article.Excerpt ?? "", pattern)
            || EF.Functions.ILike(item.article.ContentText ?? "", pattern)
            || EF.Functions.ILike(item.article.Author ?? "", pattern)
            || EF.Functions.ILike(item.article.Url, pattern)
            || EF.Functions.ILike(item.feed.Name, pattern));
    }

    query = query.Where(item => !item.article.IsHidden);
    if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Todas", StringComparison.OrdinalIgnoreCase))
        query = query.Where(item => (item.article.Category ?? "Geral") == category);
    if (feedId.HasValue)
        query = query.Where(item => item.article.FeedId == feedId.Value);
    if (favoritesOnly)
        query = query.Where(item => item.article.IsFavorite);
    if (periodHours is > 0)
        query = query.Where(item => item.article.CollectedAt >= DateTimeOffset.UtcNow.AddHours(-periodHours.Value));

    var ordered = sort?.Equals("collected", StringComparison.OrdinalIgnoreCase) == true
        ? query.OrderByDescending(item => item.article.CollectedAt).ThenByDescending(item => item.article.Id)
        : query.OrderByDescending(item => item.article.PublishedAt ?? item.article.CollectedAt).ThenByDescending(item => item.article.Id);
    var totalCount = await query.CountAsync(cancellationToken);
    var currentPageSize = pageSize == 0 ? Math.Max(totalCount, 1) : Math.Clamp(pageSize ?? 10, 1, 100);
    var currentPage = Math.Max(page ?? 1, 1);
    var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)currentPageSize));
    currentPage = Math.Min(currentPage, totalPages);
    var articles = await ordered
        .Skip((currentPage - 1) * currentPageSize)
        .Take(currentPageSize)
        .ToListAsync(cancellationToken);

    var categories = await db.Articles.AsNoTracking()
        .Where(article => !article.IsHidden)
        .Select(article => article.Category ?? "Geral")
        .Distinct()
        .OrderBy(item => item)
        .ToListAsync(cancellationToken);

    return Results.Ok(new ArticlePageResponse(
        articles.Select(item => new ArticleResponse(
        item.article.Id,
        item.article.FeedId,
        item.feed.Name,
        item.article.Title,
        item.article.Url,
        item.article.Author,
        item.article.Excerpt,
        item.article.ContentHtml,
        item.article.ContentText,
        item.article.ImageUrl,
        item.article.ImageBase64,
        item.article.ImageMimeType,
        item.article.Category,
        item.article.IsFavorite,
        item.article.IsHidden,
        item.article.PublishedAt,
        item.article.CollectedAt)),
        totalCount,
        currentPage,
        currentPageSize,
        totalPages,
        categories));
});

app.MapGet("/api/debug", async (IConfiguration config, ApiDiagnostics diagnostics, RabbitDiagnostics rabbit, CancellationToken cancellationToken) =>
{
    if (!config.GetValue<bool>("Debug:Enabled"))
        return Results.NotFound();

    var rabbitStatus = await rabbit.GetStatusAsync(cancellationToken);
    return Results.Ok(new
    {
        generatedAt = DateTimeOffset.UtcNow,
        api = new { status = "ok", recentErrors = diagnostics.RecentErrors },
        rabbit = rabbitStatus
    });
});

app.MapPut("/api/articles/{id:long}/favorite", async (long id, FavoriteRequest request, AppDbContext db, CancellationToken cancellationToken) =>
{
    var article = await db.Articles.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (article is null)
        return Results.NotFound();

    article.IsFavorite = request.IsFavorite;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { article.Id, article.IsFavorite });
});

app.MapPut("/api/articles/{id:long}/hidden", async (long id, HiddenRequest request, AppDbContext db, CancellationToken cancellationToken) =>
{
    var article = await db.Articles.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (article is null)
        return Results.NotFound();

    article.IsHidden = request.IsHidden;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { article.Id, article.IsHidden });
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public sealed record CreateFeedRequest(string Name, string Url);
public sealed record FavoriteRequest(bool IsFavorite);
public sealed record HiddenRequest(bool IsHidden);
public sealed record ArticleResponse(
    long Id,
    Guid FeedId,
    string FeedName,
    string Title,
    string Url,
    string? Author,
    string? Excerpt,
    string? ContentHtml,
    string? ContentText,
    string? ImageUrl,
    string? ImageBase64,
    string? ImageMimeType,
    string? Category,
    bool IsFavorite,
    bool IsHidden,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CollectedAt);
public sealed record ArticlePageResponse(
    IEnumerable<ArticleResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyList<string> Categories);
