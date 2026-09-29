using System.Text.RegularExpressions;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rebus.Bus;
using RssReader.Api.Repositories;
using RssReader.Contracts;

namespace RssReader.Api.Controllers;

[ApiController]
[Route("api/youtube/subscriptions")]
public sealed class YoutubeImportController(
    IServiceProvider services,
    IRepository<Feed> feeds,
    IBus bus,
    IConfiguration configuration) : ControllerBase
{
    private static readonly Regex ChannelIdPattern = new("^UC[A-Za-z0-9_-]{22}$", RegexOptions.Compiled);

    [HttpPost("import")]
    public async Task<IActionResult> Import(ImportYoutubeSubscriptionsRequest request, CancellationToken cancellationToken)
    {
        var projectId = configuration["Firebase:ProjectId"];
        if (string.IsNullOrWhiteSpace(projectId) || services.GetService<FirebaseAuth>() is not { } firebaseAuth)
            return Problem("A autenticação Firebase não foi configurada na API.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || authorization.Length <= 7) return Unauthorized();
        try
        {
            await firebaseAuth.VerifyIdTokenAsync(authorization[7..], cancellationToken);
        }
        catch (FirebaseAuthException)
        {
            return Unauthorized();
        }

        if (request.Channels is null || request.Channels.Count > 500)
            return BadRequest(new { error = "A importação aceita até 500 canais por solicitação." });
        if (request.Channels.Any(channel => channel is null || !ChannelIdPattern.IsMatch(channel.ChannelId ?? string.Empty)))
            return BadRequest(new { error = "A lista contém um ID de canal inválido." });

        var imported = 0;
        foreach (var channel in request.Channels
                     .GroupBy(item => item.ChannelId, StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            var url = $"https://www.youtube.com/feeds/videos.xml?channel_id={Uri.EscapeDataString(channel.ChannelId)}";
            var title = channel.Title?.Trim();
            var feed = await feeds.TrackedQuery().SingleOrDefaultAsync(item => item.Url == url, cancellationToken);
            if (feed is null)
            {
                feed = new Feed
                {
                    Id = Guid.NewGuid(),
                    Name = string.IsNullOrWhiteSpace(title) ? channel.ChannelId : title[..Math.Min(title.Length, 200)],
                    Url = url,
                    Description = $"Canal do YouTube {channel.ChannelId}.",
                    PollIntervalMinutes = 0,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                feeds.Add(feed);
                await feeds.SaveChangesAsync(cancellationToken);
            }

            await bus.Send(new IngestFeedCommand(feed.Id, feed.Url, Guid.NewGuid()));
            imported++;
        }

        return Accepted(new { imported, message = "Inscrições enviadas para processamento." });
    }
}

public sealed record ImportYoutubeSubscriptionsRequest(IReadOnlyList<YoutubeChannelRequest>? Channels);
public sealed record YoutubeChannelRequest(string ChannelId, string? Title);
