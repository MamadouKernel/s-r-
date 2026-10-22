using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Infrastructure.Persistence;

internal static class ProductMapper
{
    public static ProductRow ToRow(Product product)
    {
        var row = new ProductRow
        {
            Id = product.Id.Value,
            TenantId = product.Tenant.Value,
            TracksStock = product.TracksStock,
            CreatedAt = product.CreatedAt.ToUniversalTime(),
        };
        Apply(row, product);
        return row;
    }

    /// <summary>Reporte l'état changeant (fiche, activation, quantité).</summary>
    public static void Apply(ProductRow row, Product product)
    {
        row.Name = product.Name;
        row.Barcode = product.Barcode;
        row.Category = product.Category;
        row.PurchasePrice = product.PurchasePrice.Amount;
        row.SalePrice = product.SalePrice.Amount;
        row.AlertThreshold = product.AlertThreshold;
        row.IsActive = product.IsActive;
        row.QuantityOnHand = product.QuantityOnHand;
    }

    public static Product ToDomain(ProductRow row) =>
        Product.Restore(
            new ProductId(row.Id),
            new TenantId(row.TenantId),
            row.Name,
            row.Barcode,
            row.Category,
            Money.Fcfa(row.PurchasePrice),
            Money.Fcfa(row.SalePrice),
            row.AlertThreshold,
            row.TracksStock,
            row.CreatedAt,
            row.IsActive,
            row.QuantityOnHand);

    public static StockMovementRow ToRow(StockMovement movement) =>
        new()
        {
            Id = movement.Id.Value,
            TenantId = movement.Tenant.Value,
            ProductId = movement.Product.Value,
            Kind = movement.Kind,
            QuantityDelta = movement.QuantityDelta,
            Reason = movement.Reason,
            SourceId = movement.SourceId,
            UserId = movement.User.Value,
            At = movement.At.ToUniversalTime(),
        };

    public static StockMovement ToDomain(StockMovementRow row) =>
        new(
            new StockMovementId(row.Id),
            new TenantId(row.TenantId),
            new ProductId(row.ProductId),
            row.Kind,
            row.QuantityDelta,
            row.Reason,
            row.SourceId,
            new UserId(row.UserId),
            row.At);
}
