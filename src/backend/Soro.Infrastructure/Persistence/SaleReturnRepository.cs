using Microsoft.EntityFrameworkCore;
using Soro.Application.Sales;
using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Infrastructure.Persistence;

public sealed class SaleReturnRepository(SoroDbContext db) : ISaleReturnRepository
{
    public async Task<SaleReturn?> GetAsync(SaleReturnId id, TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.SaleReturns
            .AsNoTracking()
            .Include(item => item.Lines)
            .Include(item => item.Refunds)
            .SingleOrDefaultAsync(item => item.Id == id.Value && item.TenantId == tenant.Value, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<SaleReturn>> ListForSaleAsync(SaleId sale, TenantId tenant, CancellationToken cancellationToken)
    {
        var rows = await db.SaleReturns
            .AsNoTracking()
            .Include(item => item.Lines)
            .Include(item => item.Refunds)
            .Where(item => item.SaleId == sale.Value && item.TenantId == tenant.Value)
            .OrderBy(item => item.At)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDomain).ToArray();
    }

    public Task AddAsync(SaleReturn saleReturn, CancellationToken cancellationToken)
    {
        var tenant = saleReturn.Tenant.Value;
        var row = new SaleReturnRow
        {
            Id = saleReturn.Id.Value,
            TenantId = tenant,
            SaleId = saleReturn.SaleId.Value,
            RefundSessionId = saleReturn.RefundSession?.Value,
            Kind = saleReturn.Kind,
            At = saleReturn.At.ToUniversalTime(),
            ByUserId = saleReturn.By.Value,
            ApprovedBy = saleReturn.ApprovedBy?.Value,
            Reason = saleReturn.Reason,
            Total = saleReturn.Total.Amount,
        };
        for (var position = 0; position < saleReturn.Lines.Count; position++)
        {
            var line = saleReturn.Lines[position];
            row.Lines.Add(new SaleReturnLineRow
            {
                ReturnId = row.Id, Position = position, TenantId = tenant, LineIndex = line.LineIndex, Quantity = line.Quantity, Resellable = line.Resellable,
            });
        }

        for (var position = 0; position < saleReturn.Refund.Count; position++)
        {
            var part = saleReturn.Refund[position];
            row.Refunds.Add(new SaleReturnRefundRow { ReturnId = row.Id, Position = position, TenantId = tenant, Method = part.Method, Amount = part.Amount.Amount });
        }

        db.SaleReturns.Add(row);
        return Task.CompletedTask;
    }

    private static SaleReturn ToDomain(SaleReturnRow row) =>
        SaleReturn.Restore(
            new SaleReturnId(row.Id),
            new TenantId(row.TenantId),
            new SaleId(row.SaleId),
            row.Kind,
            row.At,
            new UserId(row.ByUserId),
            row.ApprovedBy is { } approver ? new UserId(approver) : null,
            row.Reason,
            row.Lines.OrderBy(line => line.Position).Select(line => new ReturnedLine(line.LineIndex, line.Quantity, line.Resellable)).ToArray(),
            row.Refunds.OrderBy(refund => refund.Position).Select(refund => new RefundPart(refund.Method, Money.Fcfa(refund.Amount))).ToArray(),
            row.RefundSessionId is { } session ? new SessionId(session) : null);
}
