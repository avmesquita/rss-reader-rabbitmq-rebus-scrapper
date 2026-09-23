using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RssReader.Worker;

public sealed class ScrapperClient(HttpClient httpClient, IConfiguration configuration)
{
    public async Task<ScrappedArticle?> ExtractAsync(string url, CancellationToken cancellationToken)
    {
        var endpoint = configuration["Scrapper:BaseUrl"] ?? "http://scrapper:3000";
        var response = await httpClient.GetFromJsonAsync<ScrapperResponse>(
            $"{endpoint.TrimEnd('/')}/api/article?url={Uri.EscapeDataString(url)}&cache=false",
            cancellationToken);
        if (response is null)
            return null;

        var title = response.Title ?? url;
        var text = response.TextContent ?? response.Excerpt ?? response.Content;
        if (IsHumanVerification(title, text))
            return null;

        var imageUrl = response.ImageUrl ?? response.OpenGraphImage ?? response.OgImage ?? response.Image;
        var (imageBase64, imageMimeType) = await DownloadImageAsync(imageUrl, cancellationToken);

        return new ScrappedArticle(
            title,
            response.Url ?? url,
            response.Byline,
            response.Excerpt,
            response.Content,
            response.TextContent,
            imageUrl,
            imageBase64,
            imageMimeType,
            response.PublishedTime);
    }

    private async Task<(string? Base64, string? MimeType)> DownloadImageAsync(string? imageUrl, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Images:StoreAsBase64") || string.IsNullOrWhiteSpace(imageUrl))
            return (null, null);

        if (imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var separator = imageUrl.IndexOf(';');
            var comma = imageUrl.IndexOf(',');
            if (separator > 5 && comma > separator)
                return (imageUrl[(comma + 1)..], imageUrl[5..separator]);
            return (null, null);
        }

        using var response = await httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return (null, null);

        var maxBytes = Math.Clamp(configuration.GetValue<int?>("Images:MaxBytes") ?? 2_000_000, 1_000, 10_000_000);
        if (response.Content.Headers.ContentLength > maxBytes)
            return (null, null);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > maxBytes)
            return (null, null);

        return (Convert.ToBase64String(bytes), response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
    }

    private static bool IsHumanVerification(string title, string? content)
    {
        var value = $"{title} {content}".ToLowerInvariant();
        return value.Contains("human verification")
            || value.Contains("let's confirm you are human")
            || value.Contains("lets confirm you are human")
            || value.Contains("verify you are human")
            || value.Contains("checking your browser");
    }
}

public sealed record ScrappedArticle(
    string Title,
    string Url,
    string? Author,
    string? Excerpt,
    string? ContentHtml,
    string? ContentText,
    string? ImageUrl,
    string? ImageBase64,
    string? ImageMimeType,
    string? PublishedTime);

internal sealed class ScrapperResponse
{
    public string? Title { get; init; }
    public string? Url { get; init; }
    public string? Byline { get; init; }
    public string? Excerpt { get; init; }
    public string? Content { get; init; }
    [JsonPropertyName("textContent")]
    public string? TextContent { get; init; }
    [JsonPropertyName("image")]
    public string? Image { get; init; }
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }
    [JsonPropertyName("openGraphImage")]
    public string? OpenGraphImage { get; init; }
    [JsonPropertyName("ogImage")]
    public string? OgImage { get; init; }
    public string? PublishedTime { get; init; }
}
