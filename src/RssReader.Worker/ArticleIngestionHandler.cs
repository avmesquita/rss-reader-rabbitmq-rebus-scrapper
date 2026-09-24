using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rebus.Handlers;
using RssReader.Contracts;
using System.Security.Cryptography;
using System.Text;

namespace RssReader.Worker;

public sealed class ArticleIngestionHandler(
    ScrapperClient scrapper,
    IDbContextFactory<WorkerDbContext> dbContextFactory,
    ILogger<ArticleIngestionHandler> logger) : IHandleMessages<ProcessArticleCommand>
{
    public async Task Handle(ProcessArticleCommand message)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var run = await db.IngestionRuns.SingleOrDefaultAsync(item => item.Id == message.RunId);
        if (run is null)
            logger.LogWarning("Execução {RunId} não encontrada para o artigo {ArticleUrl}; o artigo será persistido sem atualizar os contadores da execução.", message.RunId, message.Url);

        ScrappedArticle? article = null;
        string? error = null;
        try
        {
            logger.LogInformation("Processando artigo {ArticleUrl} da execução {RunId}.", message.Url, message.RunId);
            article = await scrapper.ExtractAsync(message.Url, message.EnclosureImage, CancellationToken.None);
            logger.LogInformation("Extração concluída para {ArticleUrl}: {Result}.", message.Url, article is null ? "fallback RSS" : "scrapper");
        }
        catch (Exception exception)
        {
            error = $"{message.Url}: {exception.Message}";
            logger.LogWarning(exception, "Falha ao extrair {ArticleUrl}; os dados do RSS serão usados.", message.Url);
            db.IngestionErrors.Add(new WorkerIngestionError
            {
                RunId = message.RunId,
                FeedId = message.FeedId,
                CreatedAt = DateTimeOffset.UtcNow,
                Stage = "Scrapper",
                ArticleUrl = message.Url,
                Message = error[..Math.Min(error.Length, 2000)]
            });
        }

        var articleUrl = article?.Url ?? message.Url;
        var normalizedUrl = ArticleIdentity.NormalizeUrl(articleUrl);
        var urlHash = ArticleIdentity.Hash(normalizedUrl);
        var title = article?.Title ?? message.Title ?? "Sem título";
        var titleHash = ArticleIdentity.Hash(title);
        var duplicate = await db.Articles.AnyAsync(item => item.UrlHash == urlHash);
        if (duplicate)
            logger.LogInformation("Artigo duplicado ignorado: {ArticleUrl}.", articleUrl);
        else
        {
            db.Articles.Add(new WorkerArticle
            {
                FeedId = message.FeedId,
                Title = title,
                Url = articleUrl,
                UrlHash = urlHash,
                TitleHash = titleHash,
                Author = article?.Author ?? message.Author,
                Excerpt = article?.Excerpt ?? message.Excerpt,
                ContentHtml = article?.ContentHtml ?? message.ContentHtml,
                ContentText = article?.ContentText ?? message.Excerpt,
                ImageUrl = article?.ImageUrl ?? message.EnclosureImage,
                ImageBase64 = article?.ImageBase64,
                ImageMimeType = article?.ImageMimeType,
                Category = message.Category ?? SourceClassifier.Classify(articleUrl),
                PublishedAt = article?.PublishedTime is not null && DateTimeOffset.TryParse(article.PublishedTime, out var published)
                    ? published.ToUniversalTime()
                    : message.PublishedAt?.ToUniversalTime(),
                CollectedAt = DateTimeOffset.UtcNow
            });
        }

        if (run is not null)
        {
            run.ProcessedCount++;
            if (!duplicate)
                run.PersistedCount++;
            if (error is not null)
                run.ErrorCount++;
            run.Status = run.ProcessedCount >= run.ItemCount
                ? run.ErrorCount == 0 ? "Succeeded" : "SucceededWithErrors"
                : "Processing";
            if (run.ProcessedCount >= run.ItemCount)
                run.CompletedAt = DateTimeOffset.UtcNow;
        }

        var source = await db.Feeds.SingleOrDefaultAsync(feed => feed.Id == message.FeedId);
        if (source is not null && run is not null)
        {
            source.LastCollectedCount = run.PersistedCount;
            source.LastError = run.ErrorCount == 0 ? null : "A execução terminou com erros em alguns artigos.";
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { ConstraintName: "IX_Articles_UrlHash" })
        {
            logger.LogInformation("Artigo duplicado ignorado após concorrência: {ArticleUrl}.", articleUrl);
            return;
        }
        if (run is not null)
            logger.LogInformation("Artigo {ArticleUrl} confirmado no banco: execução={RunId}, processados={Processed}/{Total}, persistidos={Persisted}, erros={Errors}.",
                articleUrl, run.Id, run.ProcessedCount, run.ItemCount, run.PersistedCount, run.ErrorCount);
        else
            logger.LogInformation("Artigo {ArticleUrl} confirmado no banco sem execução associada.", articleUrl);
    }
}

internal static class ArticleIdentity
{
    public static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return url.Trim();

        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        return builder.Uri.ToString().TrimEnd('/');
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));
}
