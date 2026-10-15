namespace Soro.Api.Contracts;

public sealed record SessionOpenedResponse(Guid SessionId, bool AlreadyProcessed);

public sealed record SessionClosedResponse(
    long ExpectedCashFcfa,
    long CountedCashFcfa,
    long VarianceFcfa,
    bool AlreadyProcessed);

public sealed record SaleRecordedResponse(Guid SaleId, long NetTotalFcfa, long ChangeFcfa, bool AlreadyProcessed);

/// <summary>Accusé d'une vente du lot : <c>Accepted</c>, <c>AlreadyProcessed</c> ou <c>Rejected</c> (avec code et message).</summary>
public sealed record SaleSyncStatusResponse(Guid SaleId, string Outcome, string? ErrorCode, string? Message);

public sealed record SyncSalesResponse(IReadOnlyList<SaleSyncStatusResponse> Statuses);
