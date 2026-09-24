namespace RssReader.Worker;

public sealed class ScrapperConcurrencyGate : IDisposable
{
    private readonly SemaphoreSlim semaphore;
    private int waiting;
    private int active;

    public ScrapperConcurrencyGate(IConfiguration configuration)
    {
        var capacity = Math.Max(1, configuration.GetValue<int?>("Scrapper:MaxConcurrency") ?? 1);
        Capacity = capacity;
        semaphore = new SemaphoreSlim(capacity, capacity);
    }

    public int Capacity { get; }
    public int Active => Volatile.Read(ref active);
    public int Waiting => Volatile.Read(ref waiting);

    public async Task EnterAsync(string articleUrl, ILogger logger, CancellationToken cancellationToken)
    {
        var queued = Interlocked.Increment(ref waiting);
        logger.LogInformation("Scrapper aguardando vaga para {ArticleUrl}: aguardando={Waiting}, ativos={Active}, capacidade={Capacity}.",
            articleUrl, queued, Active, Capacity);
        try
        {
            await semaphore.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref waiting);
            Interlocked.Increment(ref active);
            logger.LogInformation("Scrapper iniciou processamento de {ArticleUrl}: aguardando={Waiting}, ativos={Active}, capacidade={Capacity}.",
                articleUrl, Waiting, Active, Capacity);
        }
        catch
        {
            Interlocked.Decrement(ref waiting);
            throw;
        }
    }

    public void Exit(string articleUrl, ILogger logger)
    {
        Interlocked.Decrement(ref active);
        semaphore.Release();
        logger.LogInformation("Scrapper liberou processamento de {ArticleUrl}: aguardando={Waiting}, ativos={Active}, capacidade={Capacity}.",
            articleUrl, Waiting, Active, Capacity);
    }

    public void Dispose() => semaphore.Dispose();
}