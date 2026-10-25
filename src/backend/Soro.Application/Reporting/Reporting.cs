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

public sealed record GetTopProductsQuery(DateTimeOffset From, DateTimeOffset To, RankingCriterion By, int? Limit);

/// <summary>Classement des produits vendus (MV-01). La marge n'est ni classable ni lisible pour qui ne voit pas les prix d'achat.</summary>
public sealed class GetTopProductsHandler(ICurrentUser user, IReportingQueries queries)
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;

    public async Task<Result<IReadOnlyList<ProductRanking>>> HandleAsync(
        GetTopProductsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanViewReports(user.Role))
        {
            return Result<IReadOnlyList<ProductRanking>>.Failure(ReportingRules.Forbidden);
        }

        var canSeeMargin = RolePolicy.CanSeePurchasePrice(user.Role);
        if (query.By == RankingCriterion.Margin && !canSeeMargin)
        {
            return Result<IReadOnlyList<ProductRanking>>.Failure(
                ApplicationError.Forbidden("Ce profil ne peut pas consulter les marges."));
        }

        if (ReportingRules.CheckPeriod(query.From, query.To) is { } invalid)
        {
            return Result<IReadOnlyList<ProductRanking>>.Failure(invalid);
        }

        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        return Result<IReadOnlyList<ProductRanking>>.Success(
            await queries.GetTopProductsAsync(user.Tenant, query.From, query.To, query.By, limit, canSeeMargin, cancellationToken));
    }
}

/// <summary>Catégories du journal et les préfixes d'action qu'elles recouvrent (MV-02).</summary>
public static class AuditCategories
{
    private static readonly Dictionary<string, string[]> Prefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sales"] = ["sale."],
        ["catalogue"] = ["product.", "products.", "family."],
        ["stock"] = ["stock."],
        ["team"] = ["user."],
        ["devices"] = ["device."],
        ["credit"] = ["credit.", "debt."],
        ["subscription"] = ["subscription."],
        ["data"] = ["data."],
    };

    public static IReadOnlyCollection<string> Names => Prefixes.Keys;

    public static bool TryGetPrefixes(string category, out string[] prefixes) => Prefixes.TryGetValue(category, out prefixes!);
}

public sealed record ListAuditLogQuery(DateTimeOffset? From, DateTimeOffset? To, string? Category, int? Limit, int? Offset);

/// <summary>Journal des actions sensibles (MV-02) : propriétaire et comptable, dans leur commerce.</summary>
public sealed class ListAuditLogHandler(ICurrentUser user, IAuditLogQueries queries)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public async Task<Result<AuditPage>> HandleAsync(
        ListAuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanReadAuditLog(user.Role))
        {
            return Result<AuditPage>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas consulter le journal."));
        }

        if (ReportingRules.CheckPeriod(query.From, query.To) is { } invalid)
        {
            return Result<AuditPage>.Failure(invalid);
        }

        string[]? prefixes = null;
        if (!string.IsNullOrWhiteSpace(query.Category) && !AuditCategories.TryGetPrefixes(query.Category.Trim(), out prefixes))
        {
            return Result<AuditPage>.Failure(ApplicationError.RuleViolation(
                $"Catégorie inconnue : choisissez parmi {string.Join(", ", AuditCategories.Names)}."));
        }

        var filter = new AuditLogFilter(
            query.From,
            query.To,
            prefixes,
            Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit),
            Math.Max(query.Offset ?? 0, 0));
        return Result<AuditPage>.Success(await queries.ListAsync(user.Tenant, filter, cancellationToken));
    }
}
