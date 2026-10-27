using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Api.Contracts;

// Contrats HTTP v1. Ce ne sont ni les entités du domaine ni les lignes persistées. Les propriétés `required`
// rejettent (400) tout corps incomplet ; les règles métier (montants, remises, totaux) restent dans le domaine.
// Le commerce et l'utilisateur ne figurent jamais ici : ils viennent du jeton.

public sealed record OpenRegisterSessionRequest
{
    public required Guid SessionId { get; init; }

    public required Guid PointOfSaleId { get; init; }

    public required Guid RegisterId { get; init; }

    public required long OpeningFloatFcfa { get; init; }

    public required DateTimeOffset OpenedAt { get; init; }
}

public sealed record CloseRegisterSessionRequest
{
    public required long CountedCashFcfa { get; init; }

    public required DateTimeOffset At { get; init; }
}

public sealed record SaleLineRequest
{
    public required Guid ProductId { get; init; }

    public required string Name { get; init; }

    public required int Quantity { get; init; }

    public required long UnitPriceFcfa { get; init; }

    /// <summary>Unité de la ligne ; absente (caisse pas à jour), la ligne est lue en pièces et refusée pour un produit pesé.</summary>
    public MeasureUnit? Unit { get; init; }
}

public sealed record PaymentRequest
{
    public required Guid PaymentId { get; init; }

    public required PaymentMethod Method { get; init; }

    public required long AmountFcfa { get; init; }

    public long? TenderedFcfa { get; init; }

    public string? Reference { get; init; }
}

public sealed record RecordSaleRequest
{
    public required Guid SaleId { get; init; }

    public required Guid SessionId { get; init; }

    public required long LocalSequence { get; init; }

    public required DateTimeOffset At { get; init; }

    public required IReadOnlyList<SaleLineRequest> Lines { get; init; }

    public required long DiscountFcfa { get; init; }

    public required IReadOnlyList<PaymentRequest> Payments { get; init; }

    public Guid? CustomerId { get; init; }

    public Guid? DiscountApproverId { get; init; }

    /// <summary>Le PIN du gérant qui valide la remise : l'identifiant seul ne prouve rien (docs/decisions/0018).</summary>
    public string? DiscountApproverPin { get; init; }

    /// <summary>Le gérant qui a validé un dépassement du plafond de crédit du client (CRD-04), comme pour une remise.</summary>
    public Guid? CreditApproverId { get; init; }

    public string? CreditApproverPin { get; init; }
}

public sealed record SyncSalesRequest
{
    public required IReadOnlyList<RecordSaleRequest> Sales { get; init; }
}

public sealed record CancelSaleRequest
{
    public required string Reason { get; init; }

    /// <summary>Fixé par l'écran à son ouverture : un envoi répété ne rembourse jamais deux fois.</summary>
    public Guid? OperationId { get; init; }

    /// <summary>La caisse dont le tiroir rend les espèces ; sans elle, la session de la vente si elle est encore ouverte.</summary>
    public Guid? RegisterId { get; init; }

    /// <summary><c>false</c> : la marchandise est abîmée et ne revient pas en rayon.</summary>
    public bool Resellable { get; init; } = true;

    /// <summary>Le gérant qui valide, avec son PIN ; inutile quand l'appelant est lui-même gérant ou propriétaire.</summary>
    public Guid? ApproverId { get; init; }

    public string? ApproverPin { get; init; }
}

public sealed record ReturnLineRequest
{
    /// <summary>Position de la ligne dans la vente (0 pour la première).</summary>
    public required int LineIndex { get; init; }

    /// <summary>Pièces, ou millièmes (grammes, millilitres) pour un produit pesé.</summary>
    public required int Quantity { get; init; }

    public bool Resellable { get; init; } = true;
}

public sealed record ReturnSaleRequest
{
    public required Guid OperationId { get; init; }

    public required string Reason { get; init; }

    public required IReadOnlyList<ReturnLineRequest> Lines { get; init; }

    public Guid? RegisterId { get; init; }

    public Guid? ApproverId { get; init; }

    public string? ApproverPin { get; init; }
}
