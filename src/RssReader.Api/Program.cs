using Microsoft.EntityFrameworkCore;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using Rebus.ServiceProvider;
using RssReader.Api;
using RssReader.Contracts;
using RssReader.Api.Repositories;
using RssReader.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var workerQueue = configuration["RabbitMq:WorkerQueue"] ?? "rss-reader-worker";

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Postgres")));
builder.Services.AddControllers();
builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped<FeedService>();
builder.Services.AddScoped<ArticleService>();
builder.Services.AddSingleton<ApiDiagnostics>();
builder.Services.AddSingleton<WriteAccessService>();
builder.Services.AddHttpClient<RabbitDiagnostics>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRebus((configure, _) => configure
    .Transport(transport => transport.UseRabbitMq(configuration["RabbitMq:ConnectionString"]!, "rss-reader-api"))
    .Routing(route => route.TypeBased().Map<IngestFeedCommand>(workerQueue)));

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (exception is not null)
        context.RequestServices.GetRequiredService<ApiDiagnostics>().Record(context, exception);

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await Results.Problem("Ocorreu um erro interno na API.").ExecuteAsync(context);
}));

await DatabaseInitializer.InitializeAsync(app.Services);

app.UseCors();
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isApiWrite = path.StartsWithSegments("/api")
        && (HttpMethods.IsPost(context.Request.Method)
            || HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsPatch(context.Request.Method)
            || HttpMethods.IsDelete(context.Request.Method));
    var isUnlockRequest = path.Equals("/api/access/unlock", StringComparison.OrdinalIgnoreCase);

    if (!isApiWrite || isUnlockRequest)
    {
        await next();
        return;
    }

    var access = context.RequestServices.GetRequiredService<WriteAccessService>();
    if (!access.IsConfigured)
    {
        await Results.Problem(
            "O acesso de escrita não foi configurado pelo administrador.",
            statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
        return;
    }

    if (!access.ValidateToken(context.Request.Headers["X-Write-Token"].FirstOrDefault()))
    {
        await Results.Unauthorized().ExecuteAsync(context);
        return;
    }

    await next();
});
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();
