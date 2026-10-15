using Soro.Domain.Common;

namespace Soro.Domain.Sales;

/// <summary>
/// Vente telle que confirmée à la caisse, avant validation par le domaine.
/// L'identifiant et la séquence sont créés sur l'appareil (RG-17).
/// </summary>
public sealed record SaleDraft(
    SaleId Id,
    long LocalSequence,
    DateTimeOffset At,
    IReadOnlyList<SaleLine> Lines,
    Money Discount,
    IReadOnlyList<Payment> Payments,
    CustomerId? Customer);
