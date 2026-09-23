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
    ILogger<FeedIngestionHandler> logger) : IHandleMessages<IngestFeedCommand>
{
    public async Task Handle(IngestFeedCommand message)
    {
        using var client = httpClientFactory.CreateClient("rss");
        logger.LogInformation("Iniciando leitura do feed {FeedId} em {FeedUrl}.", message.FeedId, message.FeedUrl);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var feedExists = await db.Feeds.AnyAsync(feed => feed.Id == message.FeedId && feed.IsActive);
        if (!feedExists)
        {
            logger.LogInformation("Feed {FeedId} não existe ou está inativo; ingestão ignorada.", message.FeedId);
            return;
        }

        using var response = await client.GetAsync(message.FeedUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true });
        var feed = SyndicationFeed.Load(reader);
        var items = feed.Items.ToList();
        logger.LogInformation("Feed {FeedId} retornou {Count} itens RSS.", message.FeedId, items.Count);

        var persistedCount = 0;
        foreach (var item in items)
        {
            var link = item.Links.FirstOrDefault(itemLink => itemLink.RelationshipType is null or "alternate")?.Uri?.ToString()
                ?? item.Links.FirstOrDefault()?.Uri?.ToString()
                ?? item.Id;
            if (string.IsNullOrWhiteSpace(link))
            {
                logger.LogWarning("Item sem link ignorado no feed {FeedId}: {Title}.", message.FeedId, item.Title?.Text);
                continue;
            }

            try
            {
                var article = await scrapper.ExtractAsync(link, CancellationToken.None);
                if (article is null)
                    continue;

                db.Articles.Add(new WorkerArticle
                {
                    FeedId = message.FeedId,
                    Title = article.Title,
                    Url = article.Url,
                    Author = article.Author,
                    Excerpt = article.Excerpt,
                    ContentHtml = article.ContentHtml,
                    ContentText = article.ContentText,
                    ImageUrl = article.ImageUrl,
                    ImageBase64 = article.ImageBase64,
                    ImageMimeType = article.ImageMimeType,
                    Category = SourceClassifier.Classify(article.Url),
                    PublishedAt = DateTimeOffset.TryParse(article.PublishedTime, out var published) ? published : item.PublishDate,
                    CollectedAt = DateTimeOffset.UtcNow
                });
                persistedCount++;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Falha ao extrair o artigo {Url}; a mensagem será repetida pelo Rebus.", link);
                throw;
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Feed {FeedId} processado: {Count} itens RSS, {PersistedCount} artigos persistidos.", message.FeedId, items.Count, persistedCount);
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
