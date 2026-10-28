using Soro.Domain.Sales;

namespace Soro.Api.Contracts;

public sealed record SessionOpenedResponse(Guid SessionId, bool AlreadyProcessed);

/// <summary>
/// Le résultat d'un comptage. <c>ExpectedCashFcfa</c> et <c>VarianceFcfa</c> ne sont rendus qu'à un gérant ou au propriétaire (clôture à l'aveugle) ;
/// <c>NeedsRecount</c> : la session reste ouverte, il faut recompter.
/// </summary>
public sealed record SessionClosedResponse(
    bool Closed,
    bool NeedsRecount,
    long CountedCashFcfa,
    long? ExpectedCashFcfa,
    long? VarianceFcfa,
    bool AlreadyProcessed);

public sealed record CashMovementRecordedResponse(Guid MovementId, bool AlreadyProcessed);

public sealed record SaleRecordedResponse(Guid SaleId, long NetTotalFcfa, long ChangeFcfa, bool AlreadyProcessed);

/// <summary>Accusé d'une vente du lot : <c>Accepted</c>, <c>AlreadyProcessed</c> ou <c>Rejected</c> (avec code et message).</summary>
public sealed record SaleSyncStatusResponse(Guid SaleId, string Outcome, string? ErrorCode, string? Message);

public sealed record SyncSalesResponse(IReadOnlyList<SaleSyncStatusResponse> Statuses);

public sealed record RefundPartResponse(PaymentMethod Method, long AmountFcfa);

/// <summary>Ce que le serveur a calculé pour une annulation ou un retour : de quoi dire « rendez X en espèces », « remboursez Y par Wave ».</summary>
public sealed record ReversalResponse(Guid ReturnId, long TotalFcfa, long CashFcfa, long CreditFcfa, IReadOnlyList<RefundPartResponse> Refund, bool AlreadyProcessed);

public sealed record ApprovalGrantedResponse(string Token, Guid ApproverId, string ApproverName, DateTimeOffset ExpiresAt);
