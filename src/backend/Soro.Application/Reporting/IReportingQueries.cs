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

    /// <summary>
    /// Produits les plus vendus sur <c>[périodeDébut, périodeFin[</c>, ventes validées seulement. Quand <paramref name="includeMargin"/> est faux,
    /// la marge est toujours <c>null</c> (profil sans accès aux prix d'achat).
    /// </summary>
    /// <summary>Les chiffres bruts d'une journée (de <paramref name="dayStart"/> inclus à <paramref name="nextDayStart"/> exclu) : ventes, caissiers, mouvements d'espèces, caisses, crédit, stock bas.</summary>
    Task<DayFigures> GetDayFiguresAsync(TenantId tenant, DateTimeOffset dayStart, DateTimeOffset nextDayStart, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductRanking>> GetTopProductsAsync(
        TenantId tenant,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        RankingCriterion criterion,
        int limit,
        bool includeMargin,
        CancellationToken cancellationToken);

    Task<ActivityReport> GetActivityAsync(
        TenantId tenant,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken);
}

public sealed record AuditLogFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    IReadOnlyList<string>? ActionPrefixes,
    int Limit,
    int Offset);

/// <summary>Lecture du journal des actions sensibles : plus récentes d'abord, commerce du jeton uniquement.</summary>
public interface IAuditLogQueries
{
    Task<AuditPage> ListAsync(TenantId tenant, AuditLogFilter filter, CancellationToken cancellationToken);
}
