using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Application.Reporting;

public sealed record SalesFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    SessionId? Session,
    UserId? Cashier,
    bool IncludeCancelled,
    int Limit,
    int Offset);

public sealed record SessionsFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    SessionStatus? Status,
    bool OnlyWithVariance,
    int Limit,
    int Offset);

/// <summary>
/// Côté lecture : requêtes dédiées, sans rechargement des agrégats. Chaque méthode reçoit le tenant (RG-15).
/// Les périodes sont des intervalles <c>[début, fin[</c> déjà validés par les cas d'usage.
/// </summary>
public interface IReportingQueries
{
    Task<IReadOnlyList<SaleSummary>> ListSalesAsync(TenantId tenant, SalesFilter filter, CancellationToken cancellationToken);

    Task<SaleDetail?> GetSaleAsync(TenantId tenant, SaleId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(
        TenantId tenant,
        SessionsFilter filter,
        CancellationToken cancellationToken);

    Task<ActivityReport> GetActivityAsync(
        TenantId tenant,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken);
}
