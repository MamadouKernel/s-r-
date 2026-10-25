using Microsoft.EntityFrameworkCore;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Infrastructure.Persistence;

public sealed class ProductFamilyRepository(SoroDbContext db) : IProductFamilyRepository
{
    public async Task<ProductFamily?> GetAsync(FamilyId id, TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.ProductFamilies.SingleOrDefaultAsync(
            family => family.Id == id.Value && family.TenantId == tenant.Value,
            cancellationToken);
        return row is null ? null : ProductMapper.ToDomain(row);
    }

    public async Task<IReadOnlyList<ProductFamily>> ListAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        var rows = await db.ProductFamilies.AsNoTracking().Where(family => family.TenantId == tenant.Value).ToListAsync(cancellationToken);
        return rows.Select(ProductMapper.ToDomain).ToArray();
    }

    public Task AddAsync(ProductFamily family, CancellationToken cancellationToken)
    {
        db.ProductFamilies.Add(ProductMapper.ToRow(family));
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(ProductFamily family, CancellationToken cancellationToken)
    {
        var row = await db.ProductFamilies.FindAsync([family.Id.Value], cancellationToken)
            ?? throw new InvalidOperationException($"La famille {family.Id.Value} n'existe pas.");
        ProductMapper.Apply(row, family);
    }

}

public sealed class PhotoStore(SoroDbContext db) : IPhotoStore
{
    public async Task<Photo?> GetAsync(PhotoId id, TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.Photos.AsNoTracking().SingleOrDefaultAsync(
            photo => photo.Id == id.Value && photo.TenantId == tenant.Value && photo.RemovedAt == null,
            cancellationToken);
        return row is null ? null : new Photo(new PhotoId(row.Id), new TenantId(row.TenantId), row.ContentType, row.Data);
    }

    public Task AddAsync(Photo photo, CancellationToken cancellationToken)
    {
        db.Photos.Add(new PhotoRow
        {
            Id = photo.Id.Value,
            TenantId = photo.Tenant.Value,
            ContentType = photo.ContentType,
            Data = photo.Data,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return Task.CompletedTask;
    }

    public async Task RemoveAsync(PhotoId id, TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.Photos.SingleOrDefaultAsync(
            photo => photo.Id == id.Value && photo.TenantId == tenant.Value,
            cancellationToken);
        if (row is { RemovedAt: null })
        {
            row.Data = [];
            row.RemovedAt = DateTimeOffset.UtcNow;
        }
    }
}
