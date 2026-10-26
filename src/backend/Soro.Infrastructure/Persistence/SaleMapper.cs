using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Infrastructure.Persistence;

internal static class SaleMapper
{
    /// <summary>Clé d'unicité des références Mobile Money : insensible à la casse.</summary>
    public static string ReferenceKey(string reference) => reference.Trim().ToUpperInvariant();

    public static SaleRow ToRow(Sale sale)
    {
        var tenant = sale.Assignment.Tenant.Value;
        var row = new SaleRow
        {
            Id = sale.Id.Value,
            TenantId = tenant,
            SessionId = sale.SessionId.Value,
            PointOfSaleId = sale.Assignment.PointOfSale.Value,
            RegisterId = sale.Assignment.Register.Value,
            CashierId = sale.Assignment.Cashier.Value,
            LocalSequence = sale.LocalSequence,
            RecordedAt = sale.RecordedAt.ToUniversalTime(),
            Discount = sale.Discount.Amount,
            CustomerId = sale.CustomerId?.Value,
            GrossTotal = sale.GrossTotal.Amount,
            NetTotal = sale.NetTotal.Amount,
        };

        for (var position = 0; position < sale.Lines.Count; position++)
        {
            row.Lines.Add(ToRow(sale.Lines[position], row.Id, tenant, position));
        }

        for (var position = 0; position < sale.Payments.Count; position++)
        {
            row.Payments.Add(ToRow(sale.Payments[position], row.Id, tenant, position));
        }

        ApplyStatus(row, sale);
        return row;
    }

    /// <summary>Seule l'annulation modifie une vente après sa création (RG-02).</summary>
    public static void ApplyStatus(SaleRow row, Sale sale)
    {
        row.Status = sale.Status;
        row.CancelledBy = sale.Cancellation?.CancelledBy.Value;
        row.CancelReason = sale.Cancellation?.Reason;
        row.CancelledAt = sale.Cancellation?.At.ToUniversalTime();
    }

    public static Sale ToDomain(SaleRow row) =>
        Sale.Restore(
            new SaleId(row.Id),
            new SessionId(row.SessionId),
            new RegisterAssignment(
                new TenantId(row.TenantId),
                new PointOfSaleId(row.PointOfSaleId),
                new RegisterId(row.RegisterId),
                new UserId(row.CashierId)),
            row.LocalSequence,
            row.RecordedAt,
            row.Lines.OrderBy(line => line.Position).Select(ToDomain),
            Money.Fcfa(row.Discount),
            row.Payments.OrderBy(payment => payment.Position).Select(ToDomain),
            row.CustomerId is { } customer ? new CustomerId(customer) : null,
            Money.Fcfa(row.GrossTotal),
            Money.Fcfa(row.NetTotal),
            row.Status,
            row.CancelledBy is { } cancelledBy
                ? new Cancellation(new UserId(cancelledBy), row.CancelReason!, row.CancelledAt!.Value)
                : null);

    private static SaleLineRow ToRow(SaleLine line, Guid saleId, Guid tenant, int position) =>
        new()
        {
            SaleId = saleId,
            Position = position,
            TenantId = tenant,
            ProductId = line.ProductId.Value,
            ProductName = line.ProductName,
            Quantity = line.Quantity,
            Unit = line.Unit,
            UnitPrice = line.UnitPrice.Amount,
            UnitCost = line.UnitCost?.Amount,
        };

    private static PaymentRow ToRow(Payment payment, Guid saleId, Guid tenant, int position) =>
        new()
        {
            Id = payment.Id.Value,
            SaleId = saleId,
            Position = position,
            TenantId = tenant,
            Method = payment.Method,
            Amount = payment.Amount.Amount,
            Change = payment.Change.Amount,
            Reference = payment.Reference,
            ReferenceKey = payment.Reference is null ? null : ReferenceKey(payment.Reference),
        };

    private static SaleLine ToDomain(SaleLineRow row) =>
        SaleLine.Restore(
            new ProductId(row.ProductId), row.ProductName, row.Quantity, Money.Fcfa(row.UnitPrice), row.UnitCost is { } cost ? Money.Fcfa(cost) : null, row.Unit);

    private static Payment ToDomain(PaymentRow row) =>
        Payment.Restore(
            new PaymentId(row.Id), row.Method, Money.Fcfa(row.Amount), row.Reference, Money.Fcfa(row.Change));
}
