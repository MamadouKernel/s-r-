using Soro.Domain.Common;

namespace Soro.Domain.Sales;

/// <summary>Trace de l'annulation d'une vente : qui, pourquoi, quand (RG-02).</summary>
public sealed record Cancellation(UserId CancelledBy, string Reason, DateTimeOffset At);
