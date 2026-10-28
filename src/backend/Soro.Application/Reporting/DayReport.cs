using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Settings;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Sales;

namespace Soro.Application.Reporting;

// Rapport du jour et e-mail du soir (docs/specs/cloture-sorties-rapport CLO-06 à CLO-08, CLO-11). Aucun chiffre n'est interprété ni transformé en retenue :
// le patron juge.

public sealed record LowStockLine(string Name, int Quantity, int Threshold, MeasureUnit Unit);

/// <summary>Ce qu'un caissier a fait dans la journée : ventes, remises accordées, annulations et retours qu'il a faits.</summary>
public sealed record CashierFigures(
    Guid CashierId,
    int SalesCount,
    long NetTotalFcfa,
    int DiscountsCount,
    long DiscountFcfa,
    int CancellationsCount,
    long CancelledFcfa,
    int ReturnsCount,
    long ReturnedFcfa);

public sealed record CashMovementTotal(CashMovementKind Kind, string? Reason, int Count, long AmountFcfa);

public sealed record SessionFigures(
    Guid Id,
    Guid CashierId,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    SessionStatus Status,
    long OpeningFloatFcfa,
    long ExpectedCashFcfa,
    long? CountedCashFcfa,
    long? VarianceFcfa);

/// <summary>Les chiffres bruts d'une journée, lus par la base ; le calcul des repères (panier moyen, caisse à clôturer, écarts) est fait ensuite.</summary>
public sealed record DayFigures(
    int SalesCount,
    long NetTotalFcfa,
    IReadOnlyList<PaymentBreakdown> Payments,
    IReadOnlyList<CashierFigures> Cashiers,
    IReadOnlyList<CashMovementTotal> Movements,
    IReadOnlyList<SessionFigures> Sessions,
    long DebtCreatedFcfa,
    long RepaymentsFcfa,
    IReadOnlyList<LowStockLine> LowStock);

public sealed record CashierDay(
    Guid CashierId,
    int SalesCount,
    long NetTotalFcfa,
    int DiscountsCount,
    long DiscountFcfa,
    int CancellationsCount,
    long CancelledFcfa,
    int ReturnsCount,
    long ReturnedFcfa,
    /// <summary>Somme des écarts de ses caisses clôturées : négatif = manque, positif = excédent.</summary>
    long VarianceFcfa,
    /// <summary>Nombre de ses caisses dont l'écart dépasse la tolérance.</summary>
    int VarianceSessions);

public sealed record DaySession(
    Guid Id,
    Guid CashierId,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    SessionStatus Status,
    long OpeningFloatFcfa,
    long ExpectedCashFcfa,
    long? CountedCashFcfa,
    long? VarianceFcfa,
    /// <summary>Ouverte depuis plus longtemps que le délai d'alerte : à clôturer.</summary>
    bool NeedsClosing,
    /// <summary>Plus d'argent dans le tiroir que le seuil d'avertissement : un versement au coffre s'impose.</summary>
    bool DrawerAboveWarning);

public sealed record DayReport(
    DateOnly Day,
    int SalesCount,
    long NetTotalFcfa,
    long AverageBasketFcfa,
    IReadOnlyList<PaymentBreakdown> Payments,
    IReadOnlyList<CashierDay> Cashiers,
    IReadOnlyList<CashMovementTotal> Movements,
    IReadOnlyList<DaySession> Sessions,
    long DebtCreatedFcfa,
    long RepaymentsFcfa,
    IReadOnlyList<LowStockLine> LowStock)
{
    /// <summary>Les noms des caissiers cités, pour que l'écran dise « caisse de Awa » (le comptable ne lit pas l'annuaire).</summary>
    public IReadOnlyDictionary<Guid, string> Names { get; init; } = new Dictionary<Guid, string>();

    /// <summary>Un jour sans vente, sans caisse ouverte et sans mouvement n'envoie rien.</summary>
    public bool HasActivity => SalesCount > 0 || Movements.Count > 0 || Sessions.Count > 0;
}

public static class DayReportBuilder
{
    public static (DateTimeOffset From, DateTimeOffset To) Bounds(DateOnly day)
    {
        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return (from, from.AddDays(1));
    }

    public static DayReport Build(DayFigures figures, DateOnly day, ShopSettings settings, DateTimeOffset now, bool includeStock)
    {
        var sessions = figures.Sessions
            .Select(session => new DaySession(
                session.Id, session.CashierId, session.OpenedAt, session.ClosedAt, session.Status, session.OpeningFloatFcfa, session.ExpectedCashFcfa,
                session.CountedCashFcfa, session.VarianceFcfa,
                session.Status == SessionStatus.Open && now - session.OpenedAt > TimeSpan.FromHours(settings.OpenSessionAlertHours),
                session.Status == SessionStatus.Open && session.ExpectedCashFcfa > settings.DrawerWarningFcfa))
            .OrderBy(session => session.Status == SessionStatus.Open ? 0 : 1)
            .ThenByDescending(session => session.NeedsClosing)
            .ThenBy(session => session.OpenedAt)
            .ToArray();

        var cashiers = figures.Cashiers
            .Select(item =>
            {
                var closed = sessions.Where(session => session.CashierId == item.CashierId && session.VarianceFcfa is not null).ToArray();
                return new CashierDay(
                    item.CashierId, item.SalesCount, item.NetTotalFcfa, item.DiscountsCount, item.DiscountFcfa, item.CancellationsCount, item.CancelledFcfa,
                    item.ReturnsCount, item.ReturnedFcfa, closed.Sum(session => session.VarianceFcfa!.Value),
                    closed.Count(session => Math.Abs(session.VarianceFcfa!.Value) > settings.CashVarianceToleranceFcfa));
            })
            .ToArray();

        return new DayReport(
            day,
            figures.SalesCount,
            figures.NetTotalFcfa,
            figures.SalesCount == 0 ? 0 : (figures.NetTotalFcfa + (figures.SalesCount / 2)) / figures.SalesCount,
            figures.Payments,
            cashiers,
            figures.Movements,
            sessions,
            figures.DebtCreatedFcfa,
            figures.RepaymentsFcfa,
            includeStock ? figures.LowStock : []);
    }
}

public sealed record GetDayReportQuery(DateOnly? Day);

/// <summary>Le rapport du jour : propriétaire, gérant et comptable. Le stock n'est montré qu'à qui peut le lire.</summary>
public sealed class GetDayReportHandler(ICurrentUser user, IReportingQueries queries, IShopSettings settings, IUserRepository users, IClock clock)
{
    public async Task<Result<DayReport>> HandleAsync(GetDayReportQuery query, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanViewReports(user.Role))
        {
            return Result<DayReport>.Failure(ReportingRules.Forbidden);
        }

        var now = clock.UtcNow;
        var day = query.Day ?? DateOnly.FromDateTime(now.UtcDateTime);
        var (from, to) = DayReportBuilder.Bounds(day);
        var figures = await queries.GetDayFiguresAsync(user.Tenant, from, to, cancellationToken);
        var shop = await settings.GetAsync(user.Tenant, cancellationToken);
        var report = DayReportBuilder.Build(figures, day, shop, now, includeStock: user.Role is Role.Owner or Role.Manager);
        var cited = report.Cashiers.Select(item => item.CashierId).Concat(report.Sessions.Select(item => item.CashierId)).ToHashSet();
        var names = (await users.ListAsync(user.Tenant, cancellationToken)).Where(item => cited.Contains(item.Id.Value)).ToDictionary(item => item.Id.Value, item => item.DisplayName);
        return Result<DayReport>.Success(report with { Names = names });
    }
}
