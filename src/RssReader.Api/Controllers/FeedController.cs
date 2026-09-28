using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rebus.Bus;
using RssReader.Contracts;
using RssReader.Api.Repositories;
using RssReader.Api.Services;

namespace RssReader.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class FeedController(IRepository<Feed> feeds, IRepository<Article> articles, IRepository<FeedIngestionRun> runs, IRepository<FeedIngestionError> errors, IBus bus, FeedService service) : ControllerBase
{
    [HttpGet("feeds")]
    public async Task<IActionResult> GetFeeds(CancellationToken cancellationToken) =>
        Ok(await feeds.Query().OrderByDescending(feed => feed.CreatedAt).ToListAsync(cancellationToken));

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        var dashboardFeeds = await feeds.Query().OrderBy(feed => feed.Name).ToListAsync(cancellationToken);
        var stats = new
        {
            totalArticles = await articles.Query().CountAsync(article => !article.IsDeleted, cancellationToken),
            readArticles = await articles.Query().CountAsync(article => !article.IsDeleted && article.IsRead, cancellationToken),
            visibleArticles = await articles.Query().CountAsync(article => !article.IsDeleted && !article.IsHidden, cancellationToken),
            hiddenArticles = await articles.Query().CountAsync(article => !article.IsDeleted && article.IsHidden, cancellationToken),
            favorites = await articles.Query().CountAsync(article => !article.IsDeleted && article.IsFavorite && !article.IsHidden, cancellationToken)
        };
        return Ok(new { stats, feeds = dashboardFeeds });
    }

    [HttpGet("feeds/{id:guid}/ingestion-runs")]
    public async Task<IActionResult> GetRuns(Guid id, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (!await feeds.Query().AnyAsync(feed => feed.Id == id, cancellationToken)) return NotFound();
        var ingestionRuns = await runs.Query().Where(run => run.FeedId == id)
            .OrderByDescending(run => run.StartedAt).Take(Math.Clamp(limit ?? 20, 1, 100)).ToListAsync(cancellationToken);
        return Ok(ingestionRuns);
    }

    [HttpGet("feeds/{id:guid}/ingestion-errors")]
    public async Task<IActionResult> GetFeedErrors(Guid id, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (!await feeds.Query().AnyAsync(feed => feed.Id == id, cancellationToken)) return NotFound();
        var ingestionErrors = await errors.Query().Where(error => error.FeedId == id)
            .OrderByDescending(error => error.CreatedAt).Take(Math.Clamp(limit ?? 50, 1, 200)).ToListAsync(cancellationToken);
        return Ok(ingestionErrors);
    }

    [HttpGet("ingestion-errors")]
    public async Task<IActionResult> GetErrors([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var ingestionErrors = await (from error in errors.Query()
                            join feed in feeds.Query() on error.FeedId equals feed.Id
                            orderby error.CreatedAt descending
                            select new { error, feedName = feed.Name })
            .Take(Math.Clamp(limit ?? 100, 1, 500)).ToListAsync(cancellationToken);
        return Ok(ingestionErrors);
    }

    [HttpPost("feeds")]
    public async Task<IActionResult> CreateFeed(CreateFeedRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return BadRequest(new { error = "Informe uma URL HTTP ou HTTPS válida." });
        if (!service.ValidInterval(request.PollIntervalMinutes))
            return BadRequest(new { error = "O intervalo deve ser 0 (padrão global) ou entre 30 e 10080 minutos." });
        var feed = new Feed { Id = Guid.NewGuid(), Name = request.Name.Trim(), Url = uri.ToString(), Description = request.Description?.Trim(), PollIntervalMinutes = request.PollIntervalMinutes ?? 0, CreatedAt = DateTimeOffset.UtcNow };
        feeds.Add(feed);
        await feeds.SaveChangesAsync(cancellationToken);
        await bus.Send(new IngestFeedCommand(feed.Id, feed.Url, Guid.NewGuid()));
        return Created($"/api/feeds/{feed.Id}", feed);
    }

    [HttpPut("feeds/{id:guid}")]
    public async Task<IActionResult> UpdateFeed(Guid id, UpdateFeedRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "O nome da fonte é obrigatório." });
        if (!service.ValidInterval(request.PollIntervalMinutes)) return BadRequest(new { error = "O intervalo deve ser 0 (padrão global) ou entre 30 e 10080 minutos." });
        var feed = await feeds.TrackedQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (feed is null) return NotFound();
        feed.Name = request.Name.Trim();
        feed.Description = request.Description?.Trim();
        feed.PollIntervalMinutes = request.PollIntervalMinutes;
        if (feed.LastCheckedAt.HasValue) feed.NextScheduledAt = feed.LastCheckedAt.Value.Add(service.GetInterval(feed.PollIntervalMinutes));
        await feeds.SaveChangesAsync(cancellationToken);
        return Ok(feed);
    }

    [HttpPost("feeds/{id:guid}/refresh")]
    public async Task<IActionResult> RefreshFeed(Guid id, CancellationToken cancellationToken)
    {
        var feed = await feeds.TrackedQuery().SingleOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken);
        if (feed is null) return NotFound();
        var minimumInterval = service.GetInterval(feed.PollIntervalMinutes);
        var elapsed = feed.LastCheckedAt.HasValue ? DateTimeOffset.UtcNow - feed.LastCheckedAt.Value : minimumInterval;
        if (elapsed < minimumInterval)
        {
            var retryAfterSeconds = (int)Math.Ceiling((minimumInterval - elapsed).TotalSeconds);
            return Problem(detail: $"A fonte poderá ser atualizada novamente em {Math.Ceiling(retryAfterSeconds / 60d):0} minutos.",
                statusCode: StatusCodes.Status429TooManyRequests,
                extensions: new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfterSeconds });
        }
        var runId = Guid.NewGuid();
        await bus.Send(new IngestFeedCommand(feed.Id, feed.Url, runId));
        return Accepted($"/api/feeds/{feed.Id}", new { message = "Atualização enviada para processamento.", runId });
    }

    [HttpDelete("feeds/{id:guid}")]
    public async Task<IActionResult> DeleteFeed(Guid id, CancellationToken cancellationToken)
    {
        var feed = await feeds.TrackedQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (feed is null) return NotFound();
        await articles.Query().Where(article => article.FeedId == id).ExecuteDeleteAsync(cancellationToken);
        feeds.Remove(feed);
        await feeds.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

}
