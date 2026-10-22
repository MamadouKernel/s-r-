using Microsoft.EntityFrameworkCore;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Infrastructure.Persistence;

public sealed class StockMovementRepository(SoroDbContext db) : IStockMovementRepository
{
    public Task<bool> ExistsAsync(StockMovementId id, TenantId tenant, CancellationToken cancellationToken) =>
        db.StockMovements.AnyAsync(
            movement => movement.Id == id.Value && movement.TenantId == tenant.Value,
            cancellationToken);

    public Task AddAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        db.StockMovements.Add(ProductMapper.ToRow(movement));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<StockMovement>> ListForProductAsync(
        ProductId product,
        TenantId tenant,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await db.StockMovements.AsNoTracking()
            .Where(movement => movement.ProductId == product.Value && movement.TenantId == tenant.Value)
            .OrderByDescending(movement => movement.At)
            .ThenByDescending(movement => movement.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(ProductMapper.ToDomain).ToArray();
    }
}
