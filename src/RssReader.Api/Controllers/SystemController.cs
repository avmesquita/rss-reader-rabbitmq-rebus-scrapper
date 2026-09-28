using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using RssReader.Api.Repositories;

namespace RssReader.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class SystemController(IConfiguration configuration, IRepository<Article> articles, IRepository<FeedIngestionError> errors, ApiDiagnostics diagnostics, RabbitDiagnostics rabbit) : ControllerBase
{
    [HttpGet("debug")]
    public async Task<IActionResult> Debug(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Debug:Enabled")) return NotFound();
        return Ok(new { generatedAt = DateTimeOffset.UtcNow, api = new { status = "ok", recentErrors = diagnostics.RecentErrors }, rabbit = await rabbit.GetStatusAsync(cancellationToken) });
    }

    [HttpGet("debug/queues/{queueName}/messages")]
    public async Task<IActionResult> QueueMessages(string queueName, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Debug:Enabled")) return NotFound();
        try { return Ok(await rabbit.GetMessagesAsync(queueName, limit ?? 50, cancellationToken)); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        { return Problem("Não foi possível consultar as mensagens da fila.", statusCode: StatusCodes.Status502BadGateway); }
    }

    [HttpDelete("system/purge")]
    public async Task<IActionResult> Purge(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Debug:Enabled")) return NotFound();
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-30);
        var before = await GetStats(cancellationToken);
        var purgedArticles = await articles.Query().Where(article => article.PublishedAt < cutoffDate && !article.IsFavorite && !article.IsDeleted).ExecuteDeleteAsync(cancellationToken);
        var purgedErrors = await errors.Query().Where(error => error.CreatedAt < cutoffDate).ExecuteDeleteAsync(cancellationToken);
        var after = await GetStats(cancellationToken);
        return Ok(new { purgedArticles, purgedErrors, before, after, purgedAt = DateTimeOffset.UtcNow });
    }

    [HttpDelete("system/read-articles")]
    public async Task<IActionResult> DeleteReadArticles(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Debug:Enabled")) return NotFound();
        var before = await GetStats(cancellationToken);
        var deletedArticles = await articles.Query().Where(article => !article.IsDeleted && article.IsRead && !article.IsFavorite)
            .ExecuteUpdateAsync(update => update.SetProperty(article => article.IsDeleted, true)
                .SetProperty(article => article.Title, "Artigo removido").SetProperty(article => article.Url, "")
                .SetProperty(article => article.Author, (string?)null).SetProperty(article => article.Excerpt, (string?)null)
                .SetProperty(article => article.ContentHtml, (string?)null).SetProperty(article => article.ContentText, (string?)null)
                .SetProperty(article => article.ImageUrl, (string?)null).SetProperty(article => article.ImageBase64, (string?)null)
                .SetProperty(article => article.ImageMimeType, (string?)null).SetProperty(article => article.Category, (string?)null), cancellationToken);
        var after = await GetStats(cancellationToken);
        return Ok(new { deletedArticles, before, after, deletedAt = DateTimeOffset.UtcNow });
    }

    private async Task<object> GetStats(CancellationToken token) => new
    {
        all = await articles.Query().CountAsync(article => !article.IsDeleted, token),
        read = await articles.Query().CountAsync(article => !article.IsDeleted && article.IsRead, token),
        hidden = await articles.Query().CountAsync(article => !article.IsDeleted && article.IsHidden, token),
        favorites = await articles.Query().CountAsync(article => !article.IsDeleted && article.IsFavorite, token)
    };
}
