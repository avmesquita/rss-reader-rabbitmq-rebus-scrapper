using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Rebus.Bus;
using Rebus.Handlers;
using RssReader.Contracts;

namespace RssReader.Worker;

public sealed class FeedIngestionHandler(
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<WorkerDbContext> dbContextFactory,
    IBus bus,
    IConfiguration configuration,
    ILogger<FeedIngestionHandler> logger) : IHandleMessages<IngestFeedCommand>
{
    public async Task Handle(IngestFeedCommand message)
    {
        using var client = httpClientFactory.CreateClient("rss");
        logger.LogInformation("Iniciando leitura do feed {FeedId} em {FeedUrl}.", message.FeedId, message.FeedUrl);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var source = await db.Feeds.SingleOrDefaultAsync(feed => feed.Id == message.FeedId && feed.IsActive);
        if (source is null)
        {
            logger.LogInformation("Feed {FeedId} não existe ou está inativo; ingestão ignorada.", message.FeedId);
            return;
        }

        var run = new WorkerIngestionRun
        {
            Id = message.RunId,
            FeedId = message.FeedId,
            StartedAt = DateTimeOffset.UtcNow,
            Status = "ReadingFeed"
        };
        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync();

        try
        {
            using var response = await client.GetAsync(message.FeedUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true });
            var feed = SyndicationFeed.Load(reader);
            var commands = new List<ProcessArticleCommand>();

            foreach (var item in feed.Items)
            {
                var link = item.Links.FirstOrDefault(itemLink => itemLink.RelationshipType is null or "alternate")?.Uri?.ToString()?.Trim()
                    ?? item.Links.FirstOrDefault()?.Uri?.ToString()
                    ?? item.Id?.Trim();
                link = link is null ? null : WebUtility.HtmlDecode(link);
                if (string.IsNullOrWhiteSpace(link))
                {
                    logger.LogWarning("Item sem link ignorado no feed {FeedId}: {Title}.", message.FeedId, item.Title?.Text);
                    continue;
                }

                var enclosureImage = item.Links.FirstOrDefault(itemLink => itemLink.RelationshipType == "enclosure" && itemLink.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)?.Uri?.ToString()
                    ?? item.ElementExtensions.FirstOrDefault(extension => extension.OuterName == "enclosure")?.GetObject<System.Xml.Linq.XElement>()?.Attribute("url")?.Value;
                commands.Add(new ProcessArticleCommand(
                    message.FeedId,
                    message.RunId,
                    link,
                    item.Title?.Text,
                    item.Authors.FirstOrDefault()?.Name,
                    item.Summary?.Text,
                    item.Summary?.Text,
                    enclosureImage,
                    item.Categories.FirstOrDefault()?.Name,
                    item.PublishDate));
            }

            run.ItemCount = commands.Count;
            run.Status = commands.Count == 0 ? "Succeeded" : "Queued";
            source.LastCheckedAt = DateTimeOffset.UtcNow;
            source.NextScheduledAt = source.LastCheckedAt.Value.AddHours(Math.Max(1, configuration.GetValue<int?>("Ingestion:IntervalHours") ?? 2));
            source.LastError = commands.Count == 0 ? "O feed não retornou itens com URL válida." : null;
            source.LastCollectedCount = 0;
            await db.SaveChangesAsync();

            foreach (var command in commands)
                await bus.Send(command);

            logger.LogInformation("Feed {FeedId} confirmado: {Count} artigos enviados para a fila de artigos.", message.FeedId, commands.Count);
        }
        catch (Exception exception)
        {
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Status = "Failed";
            run.Error = exception.Message[..Math.Min(exception.Message.Length, 500)];
            run.ErrorCount++;
            source.LastCheckedAt = DateTimeOffset.UtcNow;
            source.NextScheduledAt = source.LastCheckedAt.Value.AddHours(Math.Max(1, configuration.GetValue<int?>("Ingestion:IntervalHours") ?? 2));
            source.LastError = run.Error;
            db.IngestionErrors.Add(new WorkerIngestionError
            {
                RunId = run.Id,
                FeedId = message.FeedId,
                CreatedAt = DateTimeOffset.UtcNow,
                Stage = "Feed",
                Message = run.Error
            });
            await db.SaveChangesAsync();
            logger.LogWarning(exception, "Falha ao ler/enfileirar o feed {FeedId}.", message.FeedId);
            throw;
        }
    }
}

internal static class SourceClassifier
{
    public static string Classify(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "Geral";

        var host = uri.Host.ToLowerInvariant();
        if (host.Contains("tech") || host.Contains("dev") || host.Contains("wired") || host.Contains("ars-"))
            return "Tecnologia";
        if (host.Contains("econom") || host.Contains("business") || host.Contains("finance") || host.Contains("valor"))
            return "Economia";
        if (host.Contains("sport") || host.Contains("espn") || host.Contains("futebol"))
            return "Esportes";
        if (host.Contains("science") || host.Contains("nature"))
            return "Ciência";
        return "Geral";
    }
}
