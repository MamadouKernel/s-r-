using Soro.Domain.Common;

namespace Soro.Domain.Sales;

/// <summary>
/// Ligne de vente. Le nom et le prix unitaire sont figés à la vente (RG-09, RG-21) :
/// une modification ultérieure du catalogue ne change pas l'historique. Le coût unitaire (prix d'achat moyen au moment où le serveur
/// enregistre la vente, RG-11) l'est aussi ; il manque pour un produit inconnu et pour les ventes antérieures à cette évolution.
/// </summary>
public sealed record SaleLine
{
    private SaleLine(ProductId productId, string productName, int quantity, Money unitPrice, Money? unitCost)
    {
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        UnitCost = unitCost;
    }

    public ProductId ProductId { get; }

    public string ProductName { get; }

    public int Quantity { get; }

    public Money UnitPrice { get; }

    public Money? UnitCost { get; }

    public Money Total => UnitPrice * Quantity;

    /// <summary>Reconstruction depuis le stockage : les données ont déjà été validées à la création.</summary>
    internal static SaleLine Restore(ProductId productId, string productName, int quantity, Money unitPrice, Money? unitCost) =>
        new(productId, productName, quantity, unitPrice, unitCost);

    public static SaleLine Create(ProductId productId, string productName, int quantity, Money unitPrice, Money? unitCost = null)
    {
        Ensure.That(!string.IsNullOrWhiteSpace(productName), "Le nom du produit est obligatoire.");
        Ensure.That(quantity > 0, "La quantité doit être strictement positive.");
        return new SaleLine(productId, productName.Trim(), quantity, unitPrice, unitCost);
    }
}
