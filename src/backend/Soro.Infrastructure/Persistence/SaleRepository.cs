using Microsoft.EntityFrameworkCore;
using Soro.Application.Sales;
using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Infrastructure.Persistence;

public sealed class SaleRepository(SoroDbContext db) : ISaleRepository
{
    public async Task<Sale?> GetAsync(SaleId id, TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.Sales
            .Include(sale => sale.Lines)
            .Include(sale => sale.Payments)
            .SingleOrDefaultAsync(sale => sale.Id == id.Value && sale.TenantId == tenant.Value, cancellationToken);
        return row is null ? null : SaleMapper.ToDomain(row);
    }

    public Task<bool> IsMobileMoneyReferenceUsedAsync(
        TenantId tenant,
        PaymentMethod method,
        string reference,
        CancellationToken cancellationToken)
    {
        var key = SaleMapper.ReferenceKey(reference);
        return db.Payments.AnyAsync(
            payment => payment.TenantId == tenant.Value && payment.Method == method && payment.ReferenceKey == key,
            cancellationToken);
    }

    public Task AddAsync(Sale sale, CancellationToken cancellationToken)
    {
        db.Sales.Add(SaleMapper.ToRow(sale));
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(Sale sale, CancellationToken cancellationToken)
    {
        var row = await db.Sales.FindAsync([sale.Id.Value], cancellationToken)
            ?? throw new InvalidOperationException($"La vente {sale.Id.Value} n'existe pas.");
        SaleMapper.ApplyStatus(row, sale);
    }
}
