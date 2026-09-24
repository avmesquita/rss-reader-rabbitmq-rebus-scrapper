using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http;
using Rebus.Handlers;
using RssReader.Contracts;

namespace RssReader.Worker;

public sealed class FeedIngestionHandler(
    IHttpClientFactory httpClientFactory,
    ScrapperClient scrapper,
    IDbContextFactory<WorkerDbContext> dbContextFactory,
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
            StartedAt = DateTimeOffset.UtcNow
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
            var items = feed.Items.ToList();
            run.ItemCount = items.Count;
            logger.LogInformation("Feed {FeedId} retornou {Count} itens RSS.", message.FeedId, items.Count);

            var persistedCount = 0;
            var itemErrors = new List<string>();
            foreach (var item in items)
            {
                var link = item.Links.FirstOrDefault(itemLink => itemLink.RelationshipType is null or "alternate")?.Uri?.ToString()?.Trim()
                    ?? item.Links.FirstOrDefault()?.Uri?.ToString()
                    ?? item.Id?.Trim();
                var enclosureImage = item.Links.FirstOrDefault(itemLink => itemLink.RelationshipType == "enclosure" && itemLink.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)?.Uri?.ToString()
                    ?? item.ElementExtensions.FirstOrDefault(extension => extension.OuterName == "enclosure")?.GetObject<System.Xml.Linq.XElement>()?.Attribute("url")?.Value;
                if (string.IsNullOrWhiteSpace(link))
                {
                    logger.LogWarning("Item sem link ignorado no feed {FeedId}: {Title}.", message.FeedId, item.Title?.Text);
                    continue;
                }

                ScrappedArticle? article = null;
                try
                {
                    logger.LogInformation("Processando item {ArticleUrl} do feed {FeedId}.", link, message.FeedId);
                    article = await scrapper.ExtractAsync(link, enclosureImage, CancellationToken.None);
                    logger.LogInformation("Extração concluída para {ArticleUrl}: {Result}.", link, article is null ? "fallback RSS" : "scrapper");
                }
                catch (Exception exception)
                {
                    var error = $"{link}: {exception.Message}";
                    itemErrors.Add(error[..Math.Min(error.Length, 500)]);
                    db.IngestionErrors.Add(new WorkerIngestionError
                    {
                        RunId = run.Id,
                        FeedId = message.FeedId,
                        CreatedAt = DateTimeOffset.UtcNow,
                        Stage = "Scrapper",
                        ArticleUrl = link,
                        Message = error[..Math.Min(error.Length, 2000)]
                    });
                    logger.LogWarning(exception, "Falha ao extrair o item {ArticleUrl} do feed {FeedId}; os dados do RSS serão usados.", link, message.FeedId);
                }

                var articleUrl = article?.Url ?? link;
                var publishedAt = item.PublishDate;
                if (article is not null && DateTimeOffset.TryParse(article.PublishedTime, out var published))
                    publishedAt = published;

                db.Articles.Add(new WorkerArticle
                {
                    FeedId = message.FeedId,
                    Title = article?.Title ?? item.Title?.Text ?? "Sem título",
                    Url = articleUrl,
                    Author = article?.Author ?? item.Authors.FirstOrDefault()?.Name,
                    Excerpt = article?.Excerpt ?? item.Summary?.Text,
                    ContentHtml = article?.ContentHtml ?? item.Summary?.Text,
                    ContentText = article?.ContentText ?? item.Summary?.Text,
                    ImageUrl = article?.ImageUrl ?? enclosureImage,
                    ImageBase64 = article?.ImageBase64,
                    ImageMimeType = article?.ImageMimeType,
                    Category = item.Categories.FirstOrDefault()?.Name ?? SourceClassifier.Classify(articleUrl),
                    PublishedAt = publishedAt,
                    CollectedAt = DateTimeOffset.UtcNow
                });
                persistedCount++;
                logger.LogInformation("Item {ArticleUrl} preparado para persistência no feed {FeedId}.", articleUrl, message.FeedId);
            }

            source.LastCheckedAt = DateTimeOffset.UtcNow;
            source.NextScheduledAt = source.LastCheckedAt.Value.AddHours(Math.Max(1, configuration.GetValue<int?>("Ingestion:IntervalHours") ?? 2));
            source.LastError = itemErrors.Count == 0 ? null : string.Join(" | ", itemErrors.Take(3));
            source.LastCollectedCount = persistedCount;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Status = itemErrors.Count == 0 ? "Succeeded" : "SucceededWithErrors";
            run.PersistedCount = persistedCount;
            run.ErrorCount = itemErrors.Count;
            await db.SaveChangesAsync();
            logger.LogInformation("Feed {FeedId} confirmado no banco: status={Status}, {Count} itens RSS, {PersistedCount} artigos persistidos, {ErrorCount} erros.", message.FeedId, run.Status, items.Count, persistedCount, itemErrors.Count);
        }
        catch (Exception exception)
        {
            source.LastCheckedAt = DateTimeOffset.UtcNow;
            source.NextScheduledAt = source.LastCheckedAt.Value.AddHours(Math.Max(1, configuration.GetValue<int?>("Ingestion:IntervalHours") ?? 2));
            source.LastError = exception.Message[..Math.Min(exception.Message.Length, 500)];
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Status = "Failed";
            run.Error = source.LastError;
            run.ErrorCount++;
            db.IngestionErrors.Add(new WorkerIngestionError
            {
                RunId = run.Id,
                FeedId = message.FeedId,
                CreatedAt = DateTimeOffset.UtcNow,
                Stage = "Feed",
                Message = source.LastError
            });
            await db.SaveChangesAsync();
            logger.LogWarning(exception, "Falha ao processar o feed {FeedId}; a mensagem será repetida pelo Rebus.", message.FeedId);
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
