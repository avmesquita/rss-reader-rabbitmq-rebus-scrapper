namespace RssReader.Api;

public sealed record CreateFeedRequest(string Name, string Url, string? Description = null, int? PollIntervalMinutes = null);
public sealed record UpdateFeedRequest(string Name, string? Description, int PollIntervalMinutes);
public sealed record FavoriteRequest(bool IsFavorite);
public sealed record HiddenRequest(bool IsHidden);
public sealed record ReadRequest(bool IsRead);
public sealed record UnlockRequest(string? Password);
public sealed record ArticleResponse(
    long Id,
    Guid FeedId,
    string FeedName,
    string Title,
    string Url,
    string? Author,
    string? Excerpt,
    string? ContentHtml,
    string? ContentText,
    string? ImageUrl,
    string? ImageBase64,
    string? ImageMimeType,
    string? Category,
    bool IsFavorite,
    bool IsHidden,
    bool IsRead,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CollectedAt);
public sealed record ArticlePageResponse(
    IEnumerable<ArticleResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyList<string> Categories);
