namespace RssReader.Api.Services;

public sealed class FeedService(IConfiguration configuration)
{
    public bool ValidInterval(int? minutes) => minutes is null or >= 0 and <= 10080 && minutes is not > 0 and < 30;

    public TimeSpan GetInterval(int minutes) => minutes > 0
        ? TimeSpan.FromMinutes(minutes)
        : TimeSpan.FromHours(Math.Max(1, configuration.GetValue<int?>("Ingestion:IntervalHours") ?? 2));
}
