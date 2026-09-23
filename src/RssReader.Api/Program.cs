using Microsoft.EntityFrameworkCore;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using Rebus.ServiceProvider;
using RssReader.Api;
using RssReader.Contracts;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Postgres")));
builder.Services.AddSingleton<ApiDiagnostics>();
builder.Services.AddHttpClient<RabbitDiagnostics>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRebus((configure, _) => configure
    .Transport(transport => transport.UseRabbitMq(configuration["RabbitMq:ConnectionString"]!, "rss-reader"))
    .Routing(route => route.TypeBased().Map<IngestFeedCommand>("rss-reader")));

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
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Articles\" ADD COLUMN IF NOT EXISTS \"IsFavorite\" boolean NOT NULL DEFAULT false;");
}

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/api/feeds", async (AppDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Feeds.AsNoTracking().OrderByDescending(feed => feed.CreatedAt).ToListAsync(cancellationToken)));

app.MapPost("/api/feeds", async (CreateFeedRequest request, AppDbContext db, IBus bus, CancellationToken cancellationToken) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        return Results.BadRequest(new { error = "Informe uma URL HTTP ou HTTPS válida." });

    var feed = new Feed { Id = Guid.NewGuid(), Name = request.Name.Trim(), Url = uri.ToString(), CreatedAt = DateTimeOffset.UtcNow };
    db.Feeds.Add(feed);
    await db.SaveChangesAsync(cancellationToken);
    await bus.Send(new IngestFeedCommand(feed.Id, feed.Url));
    return Results.Created($"/api/feeds/{feed.Id}", feed);
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

app.MapGet("/api/articles", async (AppDbContext db, string? search, int? limit, CancellationToken cancellationToken) =>
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

    var ordered = query.OrderByDescending(item => item.article.CollectedAt);
    var articles = limit == 0
        ? await ordered.ToListAsync(cancellationToken)
        : await ordered.Take(Math.Clamp(limit ?? 50, 1, 5000)).ToListAsync(cancellationToken);

    return Results.Ok(articles.Select(item => new ArticleResponse(
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
        item.article.PublishedAt,
        item.article.CollectedAt)));
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

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public sealed record CreateFeedRequest(string Name, string Url);
public sealed record FavoriteRequest(bool IsFavorite);
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
    DateTimeOffset? PublishedAt,
    DateTimeOffset CollectedAt);
