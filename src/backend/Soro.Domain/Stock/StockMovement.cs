using Soro.Domain.Common;

namespace Soro.Domain.Stock;

public enum StockMovementKind
{
    Reception,
    Sale,
    Loss,
    SaleCancellation,
}

/// <summary>
/// Variation de stock signée, attribuée à une personne (STK-03). Jamais modifiée ni supprimée :
/// une correction est un mouvement inverse motivé.
/// </summary>
public sealed record StockMovement(
    StockMovementId Id,
    TenantId Tenant,
    ProductId Product,
    StockMovementKind Kind,
    int QuantityDelta,
    string? Reason,
    Guid? SourceId,
    UserId User,
    DateTimeOffset At);
