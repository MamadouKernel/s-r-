using Soro.Domain.Common;

namespace Soro.Domain.Stock;

/// <summary>
/// Fiche produit et quantité en stock (CAT-01, CAT-02, STK-01 à STK-05). Chaque opération de stock renvoie le
/// mouvement correspondant, que l'appelant doit conserver : la quantité est la somme de ses mouvements.
/// </summary>
public sealed class Product
{
    private const int MaxNameLength = 200;
    private const int MaxBarcodeLength = 64;
    private const int MaxCategoryLength = 100;
    private const int MaxReasonLength = 500;

    private Product(
        ProductId id,
        TenantId tenant,
        string name,
        string? barcode,
        string? category,
        Money purchasePrice,
        Money salePrice,
        int alertThreshold,
        bool tracksStock,
        DateTimeOffset createdAt,
        FamilyId? family = null,
        string? formatLabel = null,
        PhotoId? photo = null,
        MeasureUnit unit = MeasureUnit.Piece)
    {
        Unit = unit;
        Id = id;
        FamilyId = family ?? new FamilyId(id.Value);
        FormatLabel = formatLabel;
        PhotoId = photo;
        Tenant = tenant;
        Name = name;
        Barcode = barcode;
        Category = category;
        PurchasePrice = purchasePrice;
        SalePrice = salePrice;
        AlertThreshold = alertThreshold;
        TracksStock = tracksStock;
        CreatedAt = createdAt;
        IsActive = true;
    }

    public ProductId Id { get; }

    public TenantId Tenant { get; }

    /// <summary>Famille du produit (docs/decisions/0009). Un produit seul est sa propre famille : même identifiant.</summary>
    public FamilyId FamilyId { get; private set; }

    /// <summary>Format dans la famille (« 1,5 L », « 50 kg ») ; absent pour une famille à un seul format.</summary>
    public string? FormatLabel { get; private set; }

    /// <summary>Photo propre à ce format ; sans elle, celle de la famille s'applique.</summary>
    public PhotoId? PhotoId { get; private set; }

    /// <summary>Nom affiché : « Famille · libellé » (ou la famille seule).</summary>
    public string Name { get; private set; }

    public string? Barcode { get; private set; }

    public string? Category { get; private set; }

    /// <summary>Unité de vente : pièce, kilogramme ou litre (docs/decisions/0017). Pesé ou mesuré, la quantité et le stock sont comptés en millièmes.</summary>
    public MeasureUnit Unit { get; private set; }

    public Money PurchasePrice { get; private set; }

    public Money SalePrice { get; private set; }

    public int AlertThreshold { get; private set; }

    /// <summary>Faux pour une prestation sans stock (salon, atelier…).</summary>
    public bool TracksStock { get; }

    public DateTimeOffset CreatedAt { get; }

    public bool IsActive { get; private set; }

    public int QuantityOnHand { get; private set; }

    public bool IsLowStock => TracksStock && AlertThreshold > 0 && QuantityOnHand <= AlertThreshold;

    public static Product Create(
        ProductId id,
        TenantId tenant,
        string name,
        string? barcode,
        string? category,
        Money purchasePrice,
        Money salePrice,
        int alertThreshold,
        bool tracksStock,
        DateTimeOffset createdAt,
        FamilyId? family = null,
        string? formatLabel = null,
        MeasureUnit unit = MeasureUnit.Piece)
    {
        var product = new Product(
            id, tenant, string.Empty, null, null, purchasePrice, salePrice, alertThreshold, tracksStock, createdAt, family, formatLabel, null, unit);
        product.Edit(name, barcode, category, purchasePrice, salePrice, alertThreshold);
        return product;
    }

    /// <summary>Reconstruction depuis le stockage : les données ont déjà été validées à la création.</summary>
    public static Product Restore(
        ProductId id,
        TenantId tenant,
        string name,
        string? barcode,
        string? category,
        Money purchasePrice,
        Money salePrice,
        int alertThreshold,
        bool tracksStock,
        DateTimeOffset createdAt,
        bool isActive,
        int quantityOnHand,
        FamilyId? family = null,
        string? formatLabel = null,
        PhotoId? photo = null,
        MeasureUnit unit = MeasureUnit.Piece) =>
        new(id, tenant, name, barcode, category, purchasePrice, salePrice, alertThreshold, tracksStock, createdAt, family, formatLabel, photo, unit)
        {
            IsActive = isActive,
            QuantityOnHand = quantityOnHand,
        };

    public void Edit(
        string name,
        string? barcode,
        string? category,
        Money purchasePrice,
        Money salePrice,
        int alertThreshold)
    {
        Ensure.That(!string.IsNullOrWhiteSpace(name), "Le nom du produit est obligatoire.");
        Ensure.That(name.Trim().Length <= MaxNameLength, $"Le nom ne peut pas dépasser {MaxNameLength} caractères.");
        Ensure.That(alertThreshold >= 0, "Le seuil d'alerte ne peut pas être négatif.");
        var cleanBarcode = Clean(barcode);
        var cleanCategory = Clean(category);
        Ensure.That(
            (cleanBarcode?.Length ?? 0) <= MaxBarcodeLength,
            $"Le code-barres ne peut pas dépasser {MaxBarcodeLength} caractères.");
        Ensure.That(
            (cleanCategory?.Length ?? 0) <= MaxCategoryLength,
            $"La catégorie ne peut pas dépasser {MaxCategoryLength} caractères.");

        Name = name.Trim();
        Barcode = cleanBarcode;
        Category = cleanCategory;
        PurchasePrice = purchasePrice;
        SalePrice = salePrice;
        AlertThreshold = alertThreshold;
    }

    /// <summary>Rattache le produit à une famille et recalcule son nom affiché et sa catégorie.</summary>
    public void Describe(ProductFamily family, string? label)
    {
        Ensure.That(family.Tenant == Tenant, "Cette famille n'appartient pas à ce commerce.");
        var clean = Soro.Domain.Stock.FormatLabel.Normalize(label);
        Name = Soro.Domain.Stock.FormatLabel.DisplayName(family.Name, clean);
        FamilyId = family.Id;
        FormatLabel = clean;
        Category = family.Category;
    }

    public void SetPhoto(PhotoId? photo) => PhotoId = photo;

    /// <summary>Un produit désactivé reste lisible dans l'historique mais n'est plus proposé à la vente.</summary>
    /// <summary>Change l'unité de vente d'un produit qui n'a pas de stock (les quantités changent d'échelle). L'historique est vérifié par l'appelant.</summary>
    public void ChangeUnit(MeasureUnit unit)
    {
        Ensure.That(QuantityOnHand == 0, "Cette unité ne peut plus changer : ce produit a déjà du stock.");
        Unit = unit;
    }

    /// <summary>Donne à un produit sans code-barres son code interne (ETQ-01). Un produit n'en reçoit jamais un second.</summary>
    public void AssignBarcode(string barcode)
    {
        Ensure.That(Barcode is null, "Ce produit a déjà un code-barres : réimprimez-le au lieu d'en créer un autre.");
        Barcode = barcode;
    }

    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Entrée de marchandise (STK-01). Avec le prix d'achat unitaire de la livraison, le prix d'achat du produit devient la moyenne pondérée
    /// (RG-11, docs/decisions/0007) ; sans prix, il ne change pas.
    /// </summary>
    public StockMovement Receive(
        int quantity,
        string? supplier,
        UserId user,
        DateTimeOffset at,
        StockMovementId? movementId = null,
        Money? unitCost = null)
    {
        EnsureStockOperation(quantity);
        var average = unitCost is { } cost ? WeightedAverage(cost, quantity) : PurchasePrice;
        var movement = Apply(movementId, StockMovementKind.Reception, quantity, Clean(supplier), null, user, at, unitCost);
        PurchasePrice = average;
        return movement;
    }

    /// <summary>
    /// Moyenne pondérée à l'FCFA le plus proche (0,5 vers le haut). Le stock négatif (ventes hors ligne passées avant la réception, déjà
    /// valorisées) ne pèse pas : le stock utile est au moins 0.
    /// </summary>
    private Money WeightedAverage(Money deliveryCost, int deliveredQuantity)
    {
        var useful = Math.Max(QuantityOnHand, 0);
        var total = (decimal)useful * PurchasePrice.Amount + (decimal)deliveredQuantity * deliveryCost.Amount;
        var average = Math.Round(total / (useful + (decimal)deliveredQuantity), MidpointRounding.AwayFromZero);
        return Money.Fcfa((long)average);
    }

    /// <summary>Sortie hors vente : périmé, cassé, consommation interne (STK-02).</summary>
    public StockMovement RecordLoss(
        int quantity,
        string reason,
        UserId user,
        DateTimeOffset at,
        StockMovementId? movementId = null)
    {
        EnsureStockOperation(quantity);
        Ensure.That(!string.IsNullOrWhiteSpace(reason), "Le motif de la perte est obligatoire.");
        Ensure.That(quantity <= QuantityOnHand, "La perte dépasse le stock disponible.");
        return Apply(movementId, StockMovementKind.Loss, -quantity, reason.Trim(), null, user, at);
    }

    /// <summary>
    /// Décrément à la vente (STK-04). Ne bloque jamais : le stock peut devenir négatif (vente hors ligne).
    /// Renvoie <c>null</c> pour un produit sans suivi de stock.
    /// </summary>
    public StockMovement? RecordSale(int quantity, Guid saleId, UserId user, DateTimeOffset at)
    {
        Ensure.That(quantity > 0, "La quantité doit être strictement positive.");
        return TracksStock ? Apply(null, StockMovementKind.Sale, -quantity, null, saleId, user, at) : null;
    }

    /// <summary>
    /// Une vente annulée ou un retour (docs/decisions/0020) : la marchandise revient en stock. Si elle est abîmée, elle revient puis part aussitôt en
    /// perte (deux mouvements de même motif), pour que l'historique montre ce qui s'est passé sans que le stock monte. Sans effet pour un produit sans
    /// suivi de stock ; un produit désactivé peut être rendu.
    /// </summary>
    public IReadOnlyList<StockMovement> TakeBack(
        int quantity,
        bool resellable,
        StockMovementKind kind,
        string reason,
        Guid sourceId,
        UserId user,
        DateTimeOffset at)
    {
        Ensure.That(quantity > 0, "La quantité doit être strictement positive.");
        Ensure.That(kind is StockMovementKind.Return or StockMovementKind.SaleCancellation, "Ce mouvement n'est pas un retour.");
        if (!TracksStock)
        {
            return [];
        }

        var movements = new List<StockMovement> { Apply(null, kind, quantity, reason, sourceId, user, at) };
        if (!resellable)
        {
            movements.Add(Apply(null, StockMovementKind.Loss, -quantity, reason, sourceId, user, at));
        }

        return movements;
    }

    private void EnsureStockOperation(int quantity)
    {
        Ensure.That(IsActive, "Ce produit est désactivé.");
        Ensure.That(TracksStock, "Ce produit n'a pas de suivi de stock.");
        Ensure.That(quantity > 0, "La quantité doit être strictement positive.");
    }

    private StockMovement Apply(
        StockMovementId? movementId,
        StockMovementKind kind,
        int delta,
        string? reason,
        Guid? sourceId,
        UserId user,
        DateTimeOffset at,
        Money? unitCost = null)
    {
        Ensure.That((reason?.Length ?? 0) <= MaxReasonLength, $"Le motif ne peut pas dépasser {MaxReasonLength} caractères.");
        QuantityOnHand = checked(QuantityOnHand + delta);
        return new StockMovement(movementId ?? StockMovementId.New(), Tenant, Id, kind, delta, reason, sourceId, user, at, unitCost);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
