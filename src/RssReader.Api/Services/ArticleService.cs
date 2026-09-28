using Microsoft.EntityFrameworkCore;
using RssReader.Api.Repositories;

namespace RssReader.Api.Services;

public sealed class ArticleService(IRepository<Article> articles, IRepository<Feed> feeds)
{
    public async Task<ArticlePageResponse> GetPageAsync(string? search, string? category, Guid? feedId,
        bool favoritesOnly, bool? isRead, int? periodHours, string? sort, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var query = from article in articles.Query()
                    join feed in feeds.Query() on article.FeedId equals feed.Id
                    select new { article, feed };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(item => EF.Functions.ILike(item.article.Title, pattern)
                || EF.Functions.ILike(item.article.Excerpt ?? "", pattern)
                || EF.Functions.ILike(item.article.ContentText ?? "", pattern)
                || EF.Functions.ILike(item.article.Author ?? "", pattern)
                || EF.Functions.ILike(item.article.Url, pattern)
                || EF.Functions.ILike(item.feed.Name, pattern));
        }
        query = query.Where(item => !item.article.IsHidden && !item.article.IsDeleted);
        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Todas", StringComparison.OrdinalIgnoreCase)) query = query.Where(item => (item.article.Category ?? "Geral") == category);
        if (feedId.HasValue) query = query.Where(item => item.article.FeedId == feedId.Value);
        if (favoritesOnly) query = query.Where(item => item.article.IsFavorite);
        if (isRead.HasValue) query = query.Where(item => item.article.IsRead == isRead.Value);
        if (periodHours is > 0) query = query.Where(item => item.article.CollectedAt >= DateTimeOffset.UtcNow.AddHours(-periodHours.Value));
        var ordered = sort?.Equals("collected", StringComparison.OrdinalIgnoreCase) == true
            ? query.OrderByDescending(item => item.article.CollectedAt).ThenByDescending(item => item.article.Id)
            : query.OrderByDescending(item => item.article.PublishedAt ?? item.article.CollectedAt).ThenByDescending(item => item.article.Id);
        var totalCount = await query.CountAsync(cancellationToken);
        var currentPageSize = pageSize == 0 ? Math.Max(totalCount, 1) : Math.Clamp(pageSize ?? 10, 1, 100);
        var currentPage = Math.Max(page ?? 1, 1);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)currentPageSize));
        currentPage = Math.Min(currentPage, totalPages);
        var rows = await ordered.Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToListAsync(cancellationToken);
        var categories = await articles.Query().Where(article => !article.IsHidden && !article.IsDeleted)
            .Select(article => article.Category ?? "Geral").Distinct().OrderBy(item => item).ToListAsync(cancellationToken);
        var items = rows.Select(item => new ArticleResponse(item.article.Id, item.article.FeedId, item.feed.Name, item.article.Title,
            item.article.Url, item.article.Author, item.article.Excerpt, item.article.ContentHtml, item.article.ContentText,
            item.article.ImageUrl, item.article.ImageBase64, item.article.ImageMimeType, item.article.Category,
            item.article.IsFavorite, item.article.IsHidden, item.article.IsRead, item.article.PublishedAt, item.article.CollectedAt));
        return new ArticlePageResponse(items, totalCount, currentPage, currentPageSize, totalPages, categories);
    }

    public Task<Article?> FindAsync(long id, CancellationToken cancellationToken) =>
        articles.TrackedQuery().SingleOrDefaultAsync(article => article.Id == id, cancellationToken);

    public async Task<Article?> UpdateAsync(long id, Action<Article> update, CancellationToken cancellationToken)
    {
        var article = await FindAsync(id, cancellationToken);
        if (article is null) return null;
        update(article);
        await articles.SaveChangesAsync(cancellationToken);
        return article;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => articles.SaveChangesAsync(cancellationToken);
}
