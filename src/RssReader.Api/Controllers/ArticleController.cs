using Microsoft.AspNetCore.Mvc;
using RssReader.Api.Services;

namespace RssReader.Api.Controllers;

[ApiController]
[Route("api/articles")]
public sealed class ArticleController(ArticleService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetArticles([FromQuery] string? search, [FromQuery] string? category, [FromQuery] Guid? feedId,
        [FromQuery] bool favoritesOnly, [FromQuery] bool? isRead, [FromQuery] int? periodHours, [FromQuery] string? sort,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        return Ok(await service.GetPageAsync(search, category, feedId, favoritesOnly, isRead, periodHours, sort, page, pageSize, cancellationToken));
    }

    [HttpPut("{id:long}/favorite")]
    public Task<IActionResult> Favorite(long id, FavoriteRequest request, CancellationToken token) => Update(id, article => article.IsFavorite = request.IsFavorite, article => new { article.Id, article.IsFavorite }, token);
    [HttpPut("{id:long}/hidden")]
    public Task<IActionResult> Hidden(long id, HiddenRequest request, CancellationToken token) => Update(id, article => article.IsHidden = request.IsHidden, article => new { article.Id, article.IsHidden }, token);
    [HttpPut("{id:long}/read")]
    public Task<IActionResult> Read(long id, ReadRequest request, CancellationToken token) => Update(id, article => article.IsRead = request.IsRead, article => new { article.Id, article.IsRead }, token);

    private async Task<IActionResult> Update<T>(long id, Action<Article> mutate, Func<Article, T> response, CancellationToken token)
    {
        var article = await service.UpdateAsync(id, mutate, token);
        if (article is null) return NotFound();
        return Ok(response(article));
    }
}
