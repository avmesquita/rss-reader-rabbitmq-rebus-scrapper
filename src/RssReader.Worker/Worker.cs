using Microsoft.EntityFrameworkCore;
using Rebus.Bus;
using RssReader.Contracts;

namespace RssReader.Worker;

public class Worker(
    ILogger<Worker> logger,
    IDbContextFactory<WorkerDbContext> dbContextFactory,
    IBus bus,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!(configuration.GetValue<bool?>("Ingestion:SchedulerEnabled") ?? true))
        {
            logger.LogInformation("Agendador de feeds desabilitado neste worker; aguardando eventos de artigos.");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnqueueActiveFeeds(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha ao agendar a checagem das fontes.");
            }

            var intervalHours = Math.Max(1, configuration.GetValue<int?>("Ingestion:IntervalHours") ?? 2);
            await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
        }
    }

    private async Task EnqueueActiveFeeds(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var feeds = await db.Feeds
            .Where(feed => feed.IsActive && (feed.NextScheduledAt == null || feed.NextScheduledAt <= now))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        foreach (var feed in feeds)
            await bus.Send(new IngestFeedCommand(feed.Id, feed.Url, Guid.NewGuid()));

        logger.LogInformation("{Count} fontes ativas enviadas para checagem.", feeds.Count);
    }
}
