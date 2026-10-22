using Soro.Domain.Stock;

namespace Soro.Application.Stock;

/// <summary>Produit tel que le profil courant a le droit de le voir : le prix d'achat est omis pour les autres profils (CAT-03).</summary>
public sealed record ProductView(
    Guid Id,
    string Name,
    string? Barcode,
    string? Category,
    long? PurchasePriceFcfa,
    long SalePriceFcfa,
    int AlertThreshold,
    bool TracksStock,
    bool IsActive,
    int QuantityOnHand,
    bool IsLowStock)
{
    public static ProductView From(Product product, bool showPurchasePrice) =>
        new(
            product.Id.Value,
            product.Name,
            product.Barcode,
            product.Category,
            showPurchasePrice ? product.PurchasePrice.Amount : null,
            product.SalePrice.Amount,
            product.AlertThreshold,
            product.TracksStock,
            product.IsActive,
            product.QuantityOnHand,
            product.IsLowStock);
}
