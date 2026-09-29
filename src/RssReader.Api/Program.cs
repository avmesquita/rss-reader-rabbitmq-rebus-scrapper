using Microsoft.EntityFrameworkCore;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
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
var firebaseProjectId = configuration["Firebase:ProjectId"];
if (!string.IsNullOrWhiteSpace(firebaseProjectId))
{
    var firebaseApp = FirebaseApp.Create(new AppOptions { ProjectId = firebaseProjectId }, "rss-reader-auth");
    builder.Services.AddSingleton(FirebaseAuth.GetAuth(firebaseApp));
}
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
    if (!isApiWrite)
    {
        await next();
        return;
    }

    var allowedUid = configuration["Firebase:AllowedUid"];
    var firebaseAuth = context.RequestServices.GetService<FirebaseAuth>();
    if (string.IsNullOrWhiteSpace(firebaseProjectId) || string.IsNullOrWhiteSpace(allowedUid) || firebaseAuth is null)
    {
        await Results.Problem(
            "A autenticação Firebase e o usuário autorizado não foram configurados pela administração.",
            statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
        return;
    }

    var authorization = context.Request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || authorization.Length <= 7)
    {
        await Results.Unauthorized().ExecuteAsync(context);
        return;
    }

    FirebaseToken firebaseToken;
    try
    {
        firebaseToken = await firebaseAuth.VerifyIdTokenAsync(authorization[7..], context.RequestAborted);
    }
    catch (FirebaseAuthException)
    {
        await Results.Unauthorized().ExecuteAsync(context);
        return;
    }

    if (!string.Equals(firebaseToken.Uid, allowedUid, StringComparison.Ordinal))
    {
        await Results.StatusCode(StatusCodes.Status403Forbidden).ExecuteAsync(context);
        return;
    }

    await next();
});
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();
