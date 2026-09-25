namespace RssReader.Contracts;

public sealed record IngestFeedCommand(Guid FeedId, string FeedUrl, Guid RunId);

public sealed record ProcessArticleCommand(
	Guid FeedId,
	Guid RunId,
	string Url,
	string? Title,
	string? Author,
	string? Excerpt,
	string? ContentHtml,
	string? EnclosureImage,
	string? Category,
	DateTimeOffset? PublishedAt);
