namespace RssReader.Contracts;

public sealed record IngestFeedCommand(Guid FeedId, string FeedUrl, Guid RunId);
