using Microsoft.EntityFrameworkCore;
using Soro.Application.Reporting;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Infrastructure.Persistence;

/// <summary>
/// Côté lecture : projections directes depuis les tables, sans suivi des changements ni rechargement des agrégats.
/// Chaque requête filtre explicitement le commerce, en plus de la politique RLS de la base (RG-15).
/// </summary>
public sealed class ReportingQueries(SoroDbContext db) : IReportingQueries
{
    public async Task<IReadOnlyList<SaleSummary>> ListSalesAsync(
        TenantId tenant,
        SalesFilter filter,
        CancellationToken cancellationToken)
    {
        var query = db.Sales.AsNoTracking().Where(sale => sale.TenantId == tenant.Value);
        if (filter.From is { } from)
        {
            query = query.Where(sale => sale.RecordedAt >= from.ToUniversalTime());
        }

        if (filter.To is { } to)
        {
            query = query.Where(sale => sale.RecordedAt < to.ToUniversalTime());
        }

        if (filter.Session is { } session)
        {
            query = query.Where(sale => sale.SessionId == session.Value);
        }

        if (filter.Cashier is { } cashier)
        {
            query = query.Where(sale => sale.CashierId == cashier.Value);
        }

        if (!filter.IncludeCancelled)
        {
            query = query.Where(sale => sale.Status == SaleStatus.Validated);
        }

        var rows = await query
            .OrderByDescending(sale => sale.RecordedAt)
            .ThenByDescending(sale => sale.Id)
            .Skip(filter.Offset)
            .Take(filter.Limit)
            .Select(sale => new
            {
                sale.Id,
                sale.RecordedAt,
                sale.CashierId,
                sale.RegisterId,
                sale.SessionId,
                sale.NetTotal,
                sale.Discount,
                sale.Status,
                Methods = sale.Payments.OrderBy(payment => payment.Position).Select(payment => payment.Method).ToList(),
                LineCount = sale.Lines.Count,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new SaleSummary(
                row.Id, row.RecordedAt, row.CashierId, row.RegisterId, row.SessionId,
                row.NetTotal, row.Discount, row.Status, row.Methods.Distinct().ToArray(), row.LineCount))
            .ToArray();
    }

    public async Task<SaleDetail?> GetSaleAsync(TenantId tenant, SaleId id, CancellationToken cancellationToken)
    {
        var row = await db.Sales.AsNoTracking()
            .Include(sale => sale.Lines)
            .Include(sale => sale.Payments)
            .SingleOrDefaultAsync(sale => sale.Id == id.Value && sale.TenantId == tenant.Value, cancellationToken);
        if (row is null)
        {
            return null;
        }

        return new SaleDetail(
            row.Id,
            row.RecordedAt,
            row.CashierId,
            row.RegisterId,
            row.SessionId,
            row.GrossTotal,
            row.Discount,
            row.NetTotal,
            row.Status,
            row.CustomerId,
            row.Lines.OrderBy(line => line.Position)
                .Select(line => new SaleLineView(
                    line.ProductId, line.ProductName, line.Quantity, line.UnitPrice, line.UnitPrice * line.Quantity))
                .ToArray(),
            row.Payments.OrderBy(payment => payment.Position)
                .Select(payment => new PaymentView(payment.Method, payment.Amount, payment.Change, payment.Reference))
                .ToArray(),
            row.CancelledBy is { } by && row.CancelledAt is { } at
                ? new CancellationView(by, row.CancelReason ?? string.Empty, at)
                : null);
    }

    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(
        TenantId tenant,
        SessionsFilter filter,
        CancellationToken cancellationToken)
    {
        var query = db.Sessions.AsNoTracking().Where(session => session.TenantId == tenant.Value);
        if (filter.From is { } from)
        {
            query = query.Where(session => session.OpenedAt >= from.ToUniversalTime());
        }

        if (filter.To is { } to)
        {
            query = query.Where(session => session.OpenedAt < to.ToUniversalTime());
        }

        if (filter.Status is { } status)
        {
            query = query.Where(session => session.Status == status);
        }

        if (filter.OnlyWithVariance)
        {
            query = query.Where(session => session.CashVariance != null && session.CashVariance != 0);
        }

        var rows = await query
            .OrderByDescending(session => session.OpenedAt)
            .ThenByDescending(session => session.Id)
            .Skip(filter.Offset)
            .Take(filter.Limit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new SessionSummary(
                row.Id, row.CashierId, row.RegisterId, row.OpenedAt, row.OpeningFloat, row.CashReceived,
                row.OpeningFloat + row.CashReceived, row.Status, row.CountedCash, row.CashVariance, row.ClosedAt))
            .ToArray();
    }

    public async Task<ActivityReport> GetActivityAsync(
        TenantId tenant,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        var start = periodStart.ToUniversalTime();
        var end = periodEnd.ToUniversalTime();
        var inPeriod = db.Sales.AsNoTracking()
            .Where(sale => sale.TenantId == tenant.Value && sale.RecordedAt >= start && sale.RecordedAt < end);
        var validated = inPeriod.Where(sale => sale.Status == SaleStatus.Validated);
        var cancelled = inPeriod.Where(sale => sale.Status == SaleStatus.Cancelled);

        var totals = await validated
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Net = group.Sum(sale => sale.NetTotal), Discount = group.Sum(sale => sale.Discount) })
            .SingleOrDefaultAsync(cancellationToken);
        var cancelledTotals = await cancelled
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Net = group.Sum(sale => sale.NetTotal) })
            .SingleOrDefaultAsync(cancellationToken);

        var byPayment = await (
                from payment in db.Payments.AsNoTracking()
                join sale in validated on payment.SaleId equals sale.Id
                group payment by payment.Method into methodGroup
                select new { Method = methodGroup.Key, Amount = methodGroup.Sum(payment => payment.Amount), Count = methodGroup.Count() })
            .ToListAsync(cancellationToken);

        var byCashier = await validated
            .GroupBy(sale => sale.CashierId)
            .Select(group => new { Cashier = group.Key, Count = group.Count(), Net = group.Sum(sale => sale.NetTotal) })
            .ToListAsync(cancellationToken);

        var closed = db.Sessions.AsNoTracking().Where(session =>
            session.TenantId == tenant.Value
            && session.Status == SessionStatus.Closed
            && session.ClosedAt >= start
            && session.ClosedAt < end);
        var sessionsClosed = await closed.CountAsync(cancellationToken);
        var shortfall = await closed.Where(session => session.CashVariance < 0)
            .SumAsync(session => -(session.CashVariance ?? 0), cancellationToken);
        var surplus = await closed.Where(session => session.CashVariance > 0)
            .SumAsync(session => session.CashVariance ?? 0, cancellationToken);

        return new ActivityReport(
            periodStart,
            periodEnd,
            totals?.Count ?? 0,
            totals?.Net ?? 0,
            totals?.Discount ?? 0,
            cancelledTotals?.Count ?? 0,
            cancelledTotals?.Net ?? 0,
            byPayment.OrderByDescending(item => item.Amount)
                .Select(item => new PaymentBreakdown(item.Method, item.Amount, item.Count))
                .ToArray(),
            byCashier.OrderByDescending(item => item.Net)
                .Select(item => new CashierBreakdown(item.Cashier, item.Count, item.Net))
                .ToArray(),
            sessionsClosed,
            shortfall,
            surplus);
    }
}
