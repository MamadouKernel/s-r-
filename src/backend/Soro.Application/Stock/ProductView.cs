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
    bool IsLowStock,
    Guid FamilyId,
    string FamilyName,
    string? FormatLabel,
    Guid? PhotoId)
{
    /// <param name="family">Sa famille ; absente (produit ancien), le produit est sa propre famille.</param>
    public static ProductView From(Product product, bool showPurchasePrice, ProductFamily? family = null) =>
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
            product.IsLowStock,
            product.FamilyId.Value,
            family?.Name ?? product.Name,
            product.FormatLabel,
            (product.PhotoId ?? family?.PhotoId)?.Value);
}
