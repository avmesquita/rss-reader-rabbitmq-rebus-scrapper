using Microsoft.EntityFrameworkCore;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using Rebus.ServiceProvider;
using RssReader.Contracts;
using RssReader.Worker;

var builder = Host.CreateApplicationBuilder(args);
var configuration = builder.Configuration;
var workerQueue = configuration["RabbitMq:WorkerQueue"] ?? "rss-reader-worker";

builder.Services.AddHttpClient("rss", client =>
{
	client.Timeout = TimeSpan.FromSeconds(45);
	client.DefaultRequestHeaders.UserAgent.ParseAdd("RssReader/1.0 (+https://github.com/rss-reader)");
	client.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml, text/xml");
});
builder.Services.AddHttpClient<ScrapperClient>(client =>
{
	client.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddPooledDbContextFactory<WorkerDbContext>(options =>
	options.UseNpgsql(configuration.GetConnectionString("Postgres")));
builder.Services.AddHostedService<Worker>();
builder.Services.AddRebus((configure, _) => configure
	.Transport(transport => transport.UseRabbitMq(configuration["RabbitMq:ConnectionString"]!, workerQueue))
	.Routing(route => route.TypeBased().Map<IngestFeedCommand>(workerQueue))
	.Options(options =>
	{
		var workers = Math.Max(1, configuration.GetValue<int?>("Ingestion:Workers") ?? 1);
		var maxParallelism = Math.Max(1, configuration.GetValue<int?>("Ingestion:MaxParallelism") ?? 1);
		options.SetNumberOfWorkers(workers);
		options.SetMaxParallelism(maxParallelism);
	}));
builder.Services.AutoRegisterHandlersFromAssemblyOf<FeedIngestionHandler>();

var host = builder.Build();
host.Run();
