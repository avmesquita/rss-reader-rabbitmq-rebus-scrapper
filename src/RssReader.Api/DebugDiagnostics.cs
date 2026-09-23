using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace RssReader.Api;

public sealed class ApiDiagnostics
{
    private readonly ConcurrentQueue<ApiError> errors = new();

    public void Record(HttpContext context, Exception exception)
    {
        errors.Enqueue(new ApiError(DateTimeOffset.UtcNow, context.Request.Method, context.Request.Path, exception.Message));
        while (errors.Count > 50 && errors.TryDequeue(out _)) { }
    }

    public IReadOnlyCollection<ApiError> RecentErrors => errors.ToArray();
}

public sealed record ApiError(DateTimeOffset OccurredAt, string Method, string Path, string Message);

public sealed class RabbitDiagnostics(HttpClient httpClient, IConfiguration configuration)
{
    public async Task<RabbitStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var baseUrl = configuration["RabbitMq:ManagementUrl"] ?? "http://rss_rabbitmq:15672";
        var username = configuration["RabbitMq:Username"];
        var password = configuration["RabbitMq:Password"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return new RabbitStatus(false, "Credenciais do RabbitMQ não configuradas.", []);

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        try
        {
            var queues = await httpClient.GetFromJsonAsync<List<RabbitQueue>>(
                $"{baseUrl.TrimEnd('/')}/api/queues/%2F", cancellationToken) ?? [];
            var errorQueues = queues
                .Where(queue => queue.Name.Contains("error", StringComparison.OrdinalIgnoreCase)
                    || queue.Name.Contains("dead", StringComparison.OrdinalIgnoreCase))
                .Select(queue => new RabbitQueueStatus(queue.Name, queue.Messages, queue.MessagesReady, queue.MessagesUnacknowledged, queue.Consumers))
                .ToList();
            return new RabbitStatus(true, null, errorQueues);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new RabbitStatus(false, exception.Message, []);
        }
    }
}

public sealed record RabbitStatus(bool Available, string? Error, IReadOnlyList<RabbitQueueStatus> ErrorQueues);
public sealed record RabbitQueueStatus(string Name, int Messages, int MessagesReady, int MessagesUnacknowledged, int Consumers);

internal sealed class RabbitQueue
{
    public string Name { get; init; } = string.Empty;
    public int Messages { get; init; }
    [JsonPropertyName("messages_ready")]
    public int MessagesReady { get; init; }
    [JsonPropertyName("messages_unacknowledged")]
    public int MessagesUnacknowledged { get; init; }
    public int Consumers { get; init; }
}