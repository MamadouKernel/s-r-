using Soro.Domain.Cash;
using Soro.Domain.Sales;

namespace Soro.Application.Reporting;

// Vues de lecture : des projections à plat, distinctes des agrégats du domaine (REP-01 à REP-04).

public sealed record SaleSummary(
    Guid Id,
    DateTimeOffset RecordedAt,
    Guid CashierId,
    Guid RegisterId,
    Guid SessionId,
    long NetTotalFcfa,
    long DiscountFcfa,
    SaleStatus Status,
    IReadOnlyList<PaymentMethod> PaymentMethods,
    int LineCount);

public sealed record SaleLineView(Guid ProductId, string ProductName, int Quantity, long UnitPriceFcfa, long TotalFcfa);

public sealed record PaymentView(PaymentMethod Method, long AmountFcfa, long ChangeFcfa, string? Reference);

public sealed record CancellationView(Guid CancelledBy, string Reason, DateTimeOffset At);

public sealed record SaleDetail(
    Guid Id,
    DateTimeOffset RecordedAt,
    Guid CashierId,
    Guid RegisterId,
    Guid SessionId,
    long GrossTotalFcfa,
    long DiscountFcfa,
    long NetTotalFcfa,
    SaleStatus Status,
    Guid? CustomerId,
    IReadOnlyList<SaleLineView> Lines,
    IReadOnlyList<PaymentView> Payments,
    CancellationView? Cancellation);

public sealed record SessionSummary(
    Guid Id,
    Guid CashierId,
    Guid RegisterId,
    DateTimeOffset OpenedAt,
    long OpeningFloatFcfa,
    long CashReceivedFcfa,
    long ExpectedCashFcfa,
    SessionStatus Status,
    long? CountedCashFcfa,
    long? VarianceFcfa,
    DateTimeOffset? ClosedAt);

public sealed record PaymentBreakdown(PaymentMethod Method, long AmountFcfa, int Count);

public sealed record CashierBreakdown(Guid CashierId, int SalesCount, long NetTotalFcfa);

public sealed record ActivityReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int SalesCount,
    long NetTotalFcfa,
    long DiscountTotalFcfa,
    int CancelledCount,
    long CancelledTotalFcfa,
    IReadOnlyList<PaymentBreakdown> ByPaymentMethod,
    IReadOnlyList<CashierBreakdown> ByCashier,
    int SessionsClosed,
    long ShortfallFcfa,
    long SurplusFcfa);

/// <summary>Critère de classement des produits vendus (MV-01).</summary>
public enum RankingCriterion
{
    Quantity,
    Revenue,
    Margin,
}

/// <summary>
/// Un produit au classement. La marge est une <b>estimation</b> : prix de vente figé de la ligne moins le prix d'achat actuel de la fiche.
/// Elle vaut <c>null</c> quand elle n'est pas permise au profil ou quand la fiche du produit n'existe plus.
/// </summary>
/// <param name="MarginFcfa">Chiffre d'affaires moins les coûts retenus à la vente ; <c>null</c> sans droit de voir les prix d'achat.</param>
/// <param name="MarginIsEstimate">Vrai si au moins une ligne de la période n'a pas de coût retenu (vente antérieure à RG-11) : la marge utilise alors le prix d'achat actuel.</param>
public sealed record ProductRanking(Guid ProductId, string Name, int Quantity, long RevenueFcfa, long? MarginFcfa, bool MarginIsEstimate = false);

/// <summary>Une entrée du journal des actions sensibles (MV-02).</summary>
public sealed record AuditEntryView(Guid Id, DateTimeOffset At, Guid UserId, string Action, Guid ResourceId, string Detail);

public sealed record AuditPage(IReadOnlyList<AuditEntryView> Entries, bool HasMore);
