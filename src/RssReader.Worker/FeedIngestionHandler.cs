using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;
using System.Xml.Linq;
using System.Text;
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

        var run = await db.IngestionRuns.FindAsync(message.RunId);
        if (run is null)
        {
            run = new WorkerIngestionRun
            {
                Id = message.RunId,
                FeedId = message.FeedId,
                StartedAt = DateTimeOffset.UtcNow,
                Status = "ReadingFeed"
            };
            db.IngestionRuns.Add(run);
            await db.SaveChangesAsync();
        }
        else
        {
            // Em caso de retry, apenas reconfigura o estado de leitura
            run.Status = "ReadingFeed";
            run.StartedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        try
        {
            using var response = await client.GetAsync(message.FeedUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var xmlBytes = await response.Content.ReadAsByteArrayAsync();
            var document = await LoadXmlDocumentAsync(xmlBytes, response.Content.Headers.ContentType?.CharSet);
            if (document.Root?.Name.LocalName == "rss" && (string?)document.Root.Attribute("version") != "2.0")
            {
                logger.LogInformation("Normalizando versão RSS {RssVersion} para leitura do feed {FeedId}.",
                    (string?)document.Root.Attribute("version") ?? "não informada", message.FeedId);
                document.Root.SetAttributeValue("version", "2.0");
            }
            using var reader = document.CreateReader();
            var feed = SyndicationFeed.Load(reader);
            source.Description = feed.Description?.Text?.Trim() is { Length: > 0 } description
                ? description
                : source.Description;
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
            source.NextScheduledAt = source.LastCheckedAt.Value.Add(GetPollInterval(source));
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
            source.NextScheduledAt = source.LastCheckedAt.Value.Add(GetPollInterval(source));
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

    private TimeSpan GetPollInterval(WorkerFeed feed) => feed.PollIntervalMinutes > 0
        ? TimeSpan.FromMinutes(feed.PollIntervalMinutes)
        : TimeSpan.FromHours(Math.Max(1, configuration.GetValue<int?>("Ingestion:IntervalHours") ?? 2));

    private async Task<XDocument> LoadXmlDocumentAsync(byte[] bytes, string? responseCharset)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            await using var stream = new MemoryStream(bytes, writable: false);
            return await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
        }
        catch (Exception firstError) when (firstError is XmlException or ArgumentException or NotSupportedException)
        {
            var fallbacks = new List<Encoding>();
            if (!string.IsNullOrWhiteSpace(responseCharset))
            {
                try { fallbacks.Add(Encoding.GetEncoding(responseCharset.Trim(' ', '\'', '"'))); }
                catch (ArgumentException) { }
            }
            fallbacks.Add(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false));
            fallbacks.Add(Encoding.Latin1);

            foreach (var encoding in fallbacks.DistinctBy(encoding => encoding.CodePage))
            {
                try
                {
                    var text = encoding.GetString(bytes);
                    if (text.Length > 0 && text[0] == '\uFEFF')
                        text = text[1..];
                    if (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
                    {
                        var declarationEnd = text.IndexOf("?>", StringComparison.Ordinal);
                        if (declarationEnd >= 0)
                            text = text[(declarationEnd + 2)..];
                    }
                    return XDocument.Parse(text, LoadOptions.None);
                }
                catch (XmlException) { }
            }

            throw new XmlException("O XML do feed não pôde ser lido com a codificação declarada nem com UTF-8/Latin-1.", firstError);
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
