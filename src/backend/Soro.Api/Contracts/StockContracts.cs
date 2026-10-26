using Soro.Domain.Common;

namespace Soro.Api.Contracts;

public sealed record CreateProductRequest
{
    public required Guid ProductId { get; init; }

    public required string Name { get; init; }

    public string? Barcode { get; init; }

    public string? Category { get; init; }

    public required long PurchasePriceFcfa { get; init; }

    public required long SalePriceFcfa { get; init; }

    public int AlertThreshold { get; init; }

    public bool TracksStock { get; init; } = true;

    /// <summary>Unité de vente : pièce (par défaut), kilogramme ou litre (docs/decisions/0017).</summary>
    public MeasureUnit Unit { get; init; } = MeasureUnit.Piece;
}

public sealed record UpdateProductRequest
{
    /// <summary>Le nom de la famille : tous ses formats suivent.</summary>
    public required string Name { get; init; }

    /// <summary>Le format de ce produit dans sa famille (« 1,5 L »).</summary>
    public string? FormatLabel { get; init; }

    public string? Barcode { get; init; }

    public string? Category { get; init; }

    public required long PurchasePriceFcfa { get; init; }

    public required long SalePriceFcfa { get; init; }

    public int AlertThreshold { get; init; }

    /// <summary>Nouvelle unité ; absente, elle ne change pas. Refusée dès que le produit a un historique.</summary>
    public MeasureUnit? Unit { get; init; }
}

public sealed record ReceiveStockRequest
{
    public required Guid MovementId { get; init; }

    public required int Quantity { get; init; }

    public string? Supplier { get; init; }

    public long? PurchasePriceFcfa { get; init; }
}

public sealed record RecordStockLossRequest
{
    public required Guid MovementId { get; init; }

    public required int Quantity { get; init; }

    public required string Reason { get; init; }
}

public sealed record ProductCreatedResponse(Guid ProductId, bool AlreadyProcessed);

/// <param name="PurchasePriceFcfa">Le prix d'achat moyen après une réception (RG-11) ; absent après une perte.</param>
public sealed record StockChangedResponse(Guid MovementId, int QuantityOnHand, bool AlreadyProcessed, long? PurchasePriceFcfa = null);

public sealed record FormatRequest
{
    public required Guid ProductId { get; init; }

    public string? Label { get; init; }

    public string? Barcode { get; init; }

    public required long PurchasePriceFcfa { get; init; }

    public required long SalePriceFcfa { get; init; }

    public int AlertThreshold { get; init; }

    public bool TracksStock { get; init; } = true;
}

public sealed record CreateFamilyRequest
{
    public required Guid FamilyId { get; init; }

    public required string Name { get; init; }

    public string? Category { get; init; }

    public required IReadOnlyList<FormatRequest> Formats { get; init; }
}

public sealed record AddFormatRequest
{
    public required FormatRequest Format { get; init; }

    /// <summary>Libellé à donner au format actuel quand la famille n'en a qu'un, sans libellé.</summary>
    public string? LabelForExistingFormat { get; init; }
}

public sealed record UpdateFamilyRequest
{
    public required string Name { get; init; }

    public string? Category { get; init; }
}

public sealed record GroupMemberRequest(Guid ProductId, string Label);

public sealed record GroupIntoFamilyRequest
{
    public required Guid FamilyId { get; init; }

    public required string Name { get; init; }

    public string? Category { get; init; }

    public required IReadOnlyList<GroupMemberRequest> Members { get; init; }
}

public sealed record FamilyCreatedResponse(Guid FamilyId, bool AlreadyProcessed);

public sealed record PhotoChangedResponse(Guid PhotoId);

public sealed record DeclareSubscriptionPaymentRequest
{
    public required Guid PaymentId { get; init; }

    public required Soro.Domain.Subscription.SubscriptionPeriod Period { get; init; }

    public required Soro.Domain.Subscription.SubscriptionMethod Method { get; init; }

    public required string Reference { get; init; }

    public required long AmountFcfa { get; init; }
}

public sealed record SubscriptionPaymentDeclaredResponse(Guid PaymentId, bool AlreadyProcessed);

/// <summary>Le code interne créé pour un produit (EAN-13, préfixe 29).</summary>
public sealed record InternalBarcodeResponse(string Barcode);
