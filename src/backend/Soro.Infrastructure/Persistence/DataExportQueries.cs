using System.Data;
using Microsoft.EntityFrameworkCore;
using Soro.Application.Export;
using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Infrastructure.Persistence;

/// <summary>
/// Lit les données du commerce pour l'export. Tout est lu dans une seule transaction à isolation « lecture répétable » : une vente enregistrée pendant
/// la lecture n'apparaît pas à moitié (docs/specs/export-donnees DON-06). Les identifiants techniques sont résolus en noms ici : ils ne sortent pas.
/// </summary>
public sealed class DataExportQueries(SoroDbContext db) : IDataExportQueries
{
    private const string Unknown = "Inconnu";

    public async Task<ExportCounts> CountAsync(TenantId tenant, ExportPeriod period, CancellationToken cancellationToken)
    {
        var id = tenant.Value;
        var from = period.Start;
        var to = period.EndExclusive;
        var sales = db.Sales.Where(sale => sale.TenantId == id && sale.RecordedAt >= from && sale.RecordedAt < to);
        return new ExportCounts(
            await db.Products.CountAsync(product => product.TenantId == id, cancellationToken),
            await db.Customers.CountAsync(customer => customer.TenantId == id, cancellationToken),
            await db.DebtRepayments.CountAsync(repayment => repayment.TenantId == id && repayment.At >= from && repayment.At < to, cancellationToken),
            await sales.CountAsync(cancellationToken),
            await db.SaleLines.Where(line => line.TenantId == id).Join(sales, line => line.SaleId, sale => sale.Id, (line, _) => line).CountAsync(cancellationToken),
            await db.StockMovements.CountAsync(movement => movement.TenantId == id && movement.At >= from && movement.At < to, cancellationToken));
    }

    public async Task<DataExportData> ReadAsync(TenantId tenant, ExportPeriod period, CancellationToken cancellationToken)
    {
        var id = tenant.Value;
        var from = period.Start;
        var to = period.EndExclusive;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        var users = await db.Users.AsNoTracking().Where(user => user.TenantId == id).ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        var customers = await db.Customers.AsNoTracking().Where(customer => customer.TenantId == id).OrderBy(customer => customer.Name).ToListAsync(cancellationToken);
        var customerNames = customers.ToDictionary(customer => customer.Id, customer => customer.Name);
        string User(Guid userId) => users.GetValueOrDefault(userId, Unknown);

        var families = await db.ProductFamilies.AsNoTracking().Where(family => family.TenantId == id).ToDictionaryAsync(family => family.Id, family => family.Name, cancellationToken);
        var products = (await db.Products.AsNoTracking().Where(product => product.TenantId == id).ToListAsync(cancellationToken))
            .Select(product => new ProductExportRow(
                families.GetValueOrDefault(product.FamilyId, product.Name), product.FormatLabel, product.Category, product.Barcode, product.PurchasePrice,
                product.SalePrice, product.QuantityOnHand, product.AlertThreshold, product.TracksStock, product.IsActive))
            .OrderBy(product => product.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(product => product.SalePriceFcfa)
            .ToArray();

        var sales = db.Sales.AsNoTracking().Where(sale => sale.TenantId == id && sale.RecordedAt >= from && sale.RecordedAt < to);
        var saleRows = await sales.OrderBy(sale => sale.RecordedAt).ThenBy(sale => sale.LocalSequence).ToListAsync(cancellationToken);
        var methods = (await db.Payments.AsNoTracking()
                .Where(payment => payment.TenantId == id)
                .Join(sales, payment => payment.SaleId, sale => sale.Id, (payment, _) => new { payment.SaleId, payment.Method, payment.Position })
                .ToListAsync(cancellationToken))
            .GroupBy(payment => payment.SaleId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<PaymentMethod>)group.OrderBy(payment => payment.Position).Select(payment => payment.Method).Distinct().ToArray());
        var saleExport = saleRows
            .Select(sale => new SaleExportRow(
                ExportKeys.SaleKey(sale.RecordedAt, sale.LocalSequence, User(sale.CashierId)),
                sale.RecordedAt,
                sale.LocalSequence,
                User(sale.CashierId),
                sale.CustomerId is { } customer ? customerNames.GetValueOrDefault(customer) : null,
                sale.GrossTotal,
                sale.Discount,
                sale.NetTotal,
                methods.GetValueOrDefault(sale.Id, []),
                sale.Status,
                sale.CancelReason))
            .ToArray();

        var lines = await db.SaleLines.AsNoTracking()
            .Where(line => line.TenantId == id)
            .Join(sales, line => line.SaleId, sale => sale.Id, (line, sale) => new { line, sale.RecordedAt, sale.LocalSequence, sale.CashierId })
            .OrderBy(item => item.RecordedAt).ThenBy(item => item.LocalSequence).ThenBy(item => item.line.Position)
            .ToListAsync(cancellationToken);
        var lineExport = lines
            .Select(item => new SaleLineExportRow(
                ExportKeys.SaleKey(item.RecordedAt, item.LocalSequence, User(item.CashierId)),
                item.RecordedAt,
                item.LocalSequence,
                item.line.ProductName,
                item.line.Quantity,
                item.line.UnitPrice,
                item.line.UnitPrice * item.line.Quantity,
                item.line.UnitCost))
            .ToArray();

        var repayments = (await db.DebtRepayments.AsNoTracking()
                .Where(repayment => repayment.TenantId == id && repayment.At >= from && repayment.At < to)
                .OrderBy(repayment => repayment.At)
                .ToListAsync(cancellationToken))
            .Select(repayment => new RepaymentExportRow(
                repayment.At, customerNames.GetValueOrDefault(repayment.CustomerId, Unknown), repayment.Amount, repayment.Method, repayment.Reference, User(repayment.UserId)))
            .ToArray();

        var movements = (await db.StockMovements.AsNoTracking()
                .Where(movement => movement.TenantId == id && movement.At >= from && movement.At < to)
                .Join(db.Products, movement => movement.ProductId, product => product.Id, (movement, product) => new { movement, product.Name })
                .OrderBy(item => item.movement.At)
                .ToListAsync(cancellationToken))
            .Select(item => new MovementExportRow(
                item.movement.At, item.Name, item.movement.Kind, item.movement.QuantityDelta, item.movement.Reason, item.movement.UnitCost, User(item.movement.UserId)))
            .ToArray();

        var customerExport = customers
            .Select(customer => new CustomerExportRow(customer.Name, customer.Phone, customer.CreditCeiling, customer.Debt, customer.IsActive))
            .ToArray();

        await transaction.CommitAsync(cancellationToken);
        return new DataExportData(products, customerExport, repayments, saleExport, lineExport, movements);
    }
}
