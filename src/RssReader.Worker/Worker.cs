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

            var intervalHours = Math.Max(1, configuration.GetValue<int?>("Ingestion:IntervalHours") ?? 24);
            await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
        }
    }

    private async Task EnqueueActiveFeeds(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var feeds = await db.Feeds.Where(feed => feed.IsActive).AsNoTracking().ToListAsync(cancellationToken);
        foreach (var feed in feeds)
            await bus.Send(new IngestFeedCommand(feed.Id, feed.Url));

        logger.LogInformation("{Count} fontes ativas enviadas para checagem.", feeds.Count);
    }
}
