using Microsoft.EntityFrameworkCore;

namespace RssReader.Api.Repositories;

public interface IRepository<TEntity> where TEntity : class
{
    IQueryable<TEntity> Query();
    IQueryable<TEntity> TrackedQuery();
    void Add(TEntity entity);
    void Remove(TEntity entity);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class EfRepository<TEntity>(AppDbContext db) : IRepository<TEntity> where TEntity : class
{
    public IQueryable<TEntity> Query() => db.Set<TEntity>().AsNoTracking();
    public IQueryable<TEntity> TrackedQuery() => db.Set<TEntity>();
    public void Add(TEntity entity) => db.Set<TEntity>().Add(entity);
    public void Remove(TEntity entity) => db.Set<TEntity>().Remove(entity);
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
