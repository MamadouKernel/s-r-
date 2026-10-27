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
                Returned = db.SaleReturns.Where(item => item.SaleId == sale.Id && item.Kind == ReturnKind.Return).Sum(item => (long?)item.Total) ?? 0L,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new SaleSummary(
                row.Id, row.RecordedAt, row.CashierId, row.RegisterId, row.SessionId,
                row.NetTotal, row.Discount, row.Status, row.Methods.Distinct().ToArray(), row.LineCount, row.Returned))
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

        var returns = await db.SaleReturns.AsNoTracking()
            .Include(item => item.Lines)
            .Include(item => item.Refunds)
            .Where(item => item.SaleId == row.Id && item.TenantId == tenant.Value)
            .OrderBy(item => item.At)
            .ToListAsync(cancellationToken);

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
                    line.ProductId, line.ProductName, line.Quantity, line.UnitPrice, Money.Fcfa(line.UnitPrice).ForQuantity(line.Quantity, line.Unit).Amount, line.Unit))
                .ToArray(),
            row.Payments.OrderBy(payment => payment.Position)
                .Select(payment => new PaymentView(payment.Method, payment.Amount, payment.Change, payment.Reference))
                .ToArray(),
            row.CancelledBy is { } by && row.CancelledAt is { } at
                ? new CancellationView(by, row.CancelReason ?? string.Empty, at)
                : null,
            returns.Select(item => new ReturnView(
                item.Id,
                item.Kind,
                item.At,
                item.ByUserId,
                item.ApprovedBy,
                item.Reason,
                item.Total,
                item.Refunds.Where(part => part.Method == PaymentMethod.Cash).Sum(part => part.Amount),
                item.Lines.OrderBy(line => line.Position).Select(line => new ReturnLineView(line.LineIndex, line.Quantity, line.Resellable)).ToArray(),
                item.Refunds.OrderBy(part => part.Position).Select(part => new ReturnRefundView(part.Method, part.Amount)).ToArray())).ToArray());
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
                row.OpeningFloat + row.CashReceived - row.CashRefunded, row.Status, row.CountedCash, row.CashVariance, row.ClosedAt, row.CashRefunded))
            .ToArray();
    }

    public async Task<IReadOnlyList<ProductRanking>> GetTopProductsAsync(
        TenantId tenant,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        RankingCriterion criterion,
        int limit,
        bool includeMargin,
        CancellationToken cancellationToken)
    {
        var start = periodStart.ToUniversalTime();
        var end = periodEnd.ToUniversalTime();

        // Ventes validées de la période seulement : une vente annulée ne compte dans aucun classement.
        var lines = db.Sales.AsNoTracking()
            .Where(sale => sale.TenantId == tenant.Value
                && sale.Status == SaleStatus.Validated
                && sale.RecordedAt >= start
                && sale.RecordedAt < end)
            .SelectMany(sale => sale.Lines);

        var sold = await lines
            .GroupBy(line => line.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                FrozenName = group.Max(line => line.ProductName)!,
                Quantity = group.Sum(line => line.Quantity),
                // Un produit pesé compte ses quantités en millièmes : le total de chaque ligne est arrondi au franc, demi vers le haut (docs/decisions/0017).
                Revenue = group.Sum(line => (line.UnitPrice * line.Quantity + (line.Unit == MeasureUnit.Piece ? 0L : 500L)) / (line.Unit == MeasureUnit.Piece ? 1L : 1000L)),
                // RG-11 : coût retenu à la vente ; les lignes sans coût (ventes antérieures) sont comptées à part pour l'estimation.
                KnownCost = group.Sum(line => line.UnitCost != null
                    ? (line.UnitCost.Value * line.Quantity + (line.Unit == MeasureUnit.Piece ? 0L : 500L)) / (line.Unit == MeasureUnit.Piece ? 1L : 1000L)
                    : 0L),
                UncostedQuantity = group.Sum(line => line.UnitCost == null ? line.Quantity : 0),
            })
            .ToListAsync(cancellationToken);

        // Les retours de la période (pas les annulations, déjà exclues avec leur vente) viennent en déduction : quantité, chiffre d'affaires et coût.
        var returnedByProduct = await ReturnedByProductAsync(tenant, start, end, cancellationToken);

        // Fiches des produits vendus : nom actuel et prix d'achat actuel, utilisé seulement pour les lignes sans coût retenu (estimation).
        var ids = sold.Select(item => item.ProductId).ToList();
        var products = ids.Count == 0
            ? []
            : await db.Products.AsNoTracking()
                .Where(product => product.TenantId == tenant.Value && ids.Contains(product.Id))
                .Select(product => new { product.Id, product.Name, product.PurchasePrice, product.Unit })
                .ToDictionaryAsync(product => product.Id, cancellationToken);

        var ranking = sold.Where(raw => raw.Quantity > returnedByProduct.GetValueOrDefault(raw.ProductId).Quantity).Select(raw =>
        {
            var back = returnedByProduct.GetValueOrDefault(raw.ProductId);
            var item = new
            {
                raw.ProductId,
                raw.FrozenName,
                Quantity = raw.Quantity - back.Quantity,
                Revenue = raw.Revenue - back.Revenue,
                KnownCost = raw.KnownCost - back.KnownCost,
                UncostedQuantity = raw.UncostedQuantity,
            };
            products.TryGetValue(item.ProductId, out var product);
            // Sans fiche, des lignes sans coût n'ont rien pour s'estimer : marge inconnue. Sinon : coûts retenus + estimation du reste.
            long? margin = includeMargin && (item.UncostedQuantity == 0 || product is not null)
                ? item.Revenue - item.KnownCost - Money.Fcfa(product?.PurchasePrice ?? 0).ForQuantity(item.UncostedQuantity, product?.Unit ?? MeasureUnit.Piece).Amount
                : null;
            return new ProductRanking(
                item.ProductId, product?.Name ?? item.FrozenName, item.Quantity, item.Revenue, margin, includeMargin && item.UncostedQuantity > 0, product?.Unit ?? MeasureUnit.Piece);
        });

        // Critère décroissant, puis le nom pour un ordre stable ; sans marge connue, un produit passe après ceux qui en ont une.
        var ordered = criterion switch
        {
            RankingCriterion.Revenue => ranking.OrderByDescending(item => item.RevenueFcfa),
            RankingCriterion.Margin => ranking.OrderByDescending(item => item.MarginFcfa.HasValue).ThenByDescending(item => item.MarginFcfa),
            // Des grammes et des pièces ne se comparent pas : on classe sur la quantité vendue dans l'unité du produit (kg, L ou pièces).
            _ => ranking.OrderByDescending(item => (decimal)item.Quantity / item.Unit.Scale()),
        };
        return ordered.ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).Take(limit).ToArray();
    }

    /// <summary>Ce qui est revenu pendant la période, par produit : quantité, argent rendu pour ces lignes (hors remise répartie) et coût retenu.</summary>
    private async Task<Dictionary<Guid, (int Quantity, long Revenue, long KnownCost)>> ReturnedByProductAsync(
        TenantId tenant, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var returned = await (
                from line in db.Set<SaleReturnLineRow>().AsNoTracking()
                join item in db.SaleReturns.AsNoTracking() on line.ReturnId equals item.Id
                where item.TenantId == tenant.Value && item.Kind == ReturnKind.Return && item.At >= start && item.At < end
                join saleLine in db.SaleLines.AsNoTracking() on new { item.SaleId, Position = line.LineIndex } equals new { saleLine.SaleId, saleLine.Position }
                select new { saleLine.ProductId, line.Quantity, saleLine.UnitPrice, saleLine.UnitCost, saleLine.Unit })
            .ToListAsync(cancellationToken);
        return returned
            .GroupBy(item => item.ProductId)
            .ToDictionary(
                group => group.Key,
                group => (
                    group.Sum(item => item.Quantity),
                    group.Sum(item => Money.Fcfa(item.UnitPrice).ForQuantity(item.Quantity, item.Unit).Amount),
                    group.Sum(item => item.UnitCost is { } cost ? Money.Fcfa(cost).ForQuantity(item.Quantity, item.Unit).Amount : 0L)));
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

        var returnTotals = await db.SaleReturns.AsNoTracking()
            .Where(item => item.TenantId == tenant.Value && item.Kind == ReturnKind.Return && item.At >= start && item.At < end)
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Total = group.Sum(item => item.Total) })
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
            (totals?.Net ?? 0) - (returnTotals?.Total ?? 0),
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
            surplus,
            returnTotals?.Count ?? 0,
            returnTotals?.Total ?? 0);
    }
}
