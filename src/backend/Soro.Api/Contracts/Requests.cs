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
}

public sealed record SyncSalesRequest
{
    public required IReadOnlyList<RecordSaleRequest> Sales { get; init; }
}

public sealed record CancelSaleRequest
{
    public required string Reason { get; init; }
}
