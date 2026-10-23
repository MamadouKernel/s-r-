using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Application.Reporting;

internal static class ReportingRules
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
    public const int MaxPeriodDays = 366;

    public static ApplicationError Forbidden { get; } =
        ApplicationError.Forbidden("Ce profil ne peut pas consulter les ventes et les rapports.");

    public static ApplicationError? CheckPeriod(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is null || to is null)
        {
            return null;
        }

        if (from >= to)
        {
            return ApplicationError.RuleViolation("La période est invalide : le début doit précéder la fin.");
        }

        return to - from > TimeSpan.FromDays(MaxPeriodDays)
            ? ApplicationError.RuleViolation($"La période ne peut pas dépasser {MaxPeriodDays} jours.")
            : null;
    }

    public static int Limit(int? requested) => Math.Clamp(requested ?? DefaultLimit, 1, MaxLimit);

    public static int Offset(int? requested) => Math.Max(requested ?? 0, 0);
}

public sealed record ListSalesQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    SessionId? Session,
    UserId? Cashier,
    bool IncludeCancelled,
    int? Limit,
    int? Offset);

/// <summary>Liste des ventes du commerce (REP-01).</summary>
public sealed class ListSalesHandler(ICurrentUser user, IReportingQueries queries)
{
    public async Task<Result<IReadOnlyList<SaleSummary>>> HandleAsync(
        ListSalesQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanViewReports(user.Role))
        {
            return Result<IReadOnlyList<SaleSummary>>.Failure(ReportingRules.Forbidden);
        }

        if (ReportingRules.CheckPeriod(query.From, query.To) is { } invalid)
        {
            return Result<IReadOnlyList<SaleSummary>>.Failure(invalid);
        }

        var filter = new SalesFilter(
            query.From, query.To, query.Session, query.Cashier, query.IncludeCancelled,
            ReportingRules.Limit(query.Limit), ReportingRules.Offset(query.Offset));
        return Result<IReadOnlyList<SaleSummary>>.Success(
            await queries.ListSalesAsync(user.Tenant, filter, cancellationToken));
    }
}

/// <summary>Détail d'une vente (REP-02).</summary>
public sealed class GetSaleHandler(ICurrentUser user, IReportingQueries queries)
{
    public async Task<Result<SaleDetail>> HandleAsync(SaleId id, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanViewReports(user.Role))
        {
            return Result<SaleDetail>.Failure(ReportingRules.Forbidden);
        }

        var sale = await queries.GetSaleAsync(user.Tenant, id, cancellationToken);
        return sale is null
            ? Result<SaleDetail>.Failure(ApplicationError.NotFound("Vente introuvable."))
            : Result<SaleDetail>.Success(sale);
    }
}

public sealed record ListSessionsQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    SessionStatus? Status,
    bool OnlyWithVariance,
    int? Limit,
    int? Offset);

/// <summary>Sessions de caisse et écarts de clôture (REP-03).</summary>
public sealed class ListSessionsHandler(ICurrentUser user, IReportingQueries queries)
{
    public async Task<Result<IReadOnlyList<SessionSummary>>> HandleAsync(
        ListSessionsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanViewReports(user.Role))
        {
            return Result<IReadOnlyList<SessionSummary>>.Failure(ReportingRules.Forbidden);
        }

        if (ReportingRules.CheckPeriod(query.From, query.To) is { } invalid)
        {
            return Result<IReadOnlyList<SessionSummary>>.Failure(invalid);
        }

        var filter = new SessionsFilter(
            query.From, query.To, query.Status, query.OnlyWithVariance,
            ReportingRules.Limit(query.Limit), ReportingRules.Offset(query.Offset));
        return Result<IReadOnlyList<SessionSummary>>.Success(
            await queries.ListSessionsAsync(user.Tenant, filter, cancellationToken));
    }
}

public sealed record GetActivityReportQuery(DateTimeOffset From, DateTimeOffset To);

/// <summary>Rapport d'activité sur une période (REP-04).</summary>
public sealed class GetActivityReportHandler(ICurrentUser user, IReportingQueries queries)
{
    public async Task<Result<ActivityReport>> HandleAsync(
        GetActivityReportQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanViewReports(user.Role))
        {
            return Result<ActivityReport>.Failure(ReportingRules.Forbidden);
        }

        if (ReportingRules.CheckPeriod(query.From, query.To) is { } invalid)
        {
            return Result<ActivityReport>.Failure(invalid);
        }

        return Result<ActivityReport>.Success(
            await queries.GetActivityAsync(user.Tenant, query.From, query.To, cancellationToken));
    }
}
