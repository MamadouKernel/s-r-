using System.Globalization;
using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Settings;
using Soro.Application.Signup;
using Soro.Application.Subscription;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Domain.Subscription;

namespace Soro.Application.Reporting;

/// <summary>Garde la trace des e-mails du soir envoyés : un seul par commerce et par jour, même si la tâche tourne plusieurs fois.</summary>
public interface IEveningReportLog
{
    Task<bool> WasSentAsync(TenantId tenant, DateOnly day, CancellationToken cancellationToken);

    Task RecordSentAsync(TenantId tenant, DateOnly day, DateTimeOffset at, CancellationToken cancellationToken);
}

/// <summary>Le texte de l'e-mail du soir : en français, des mots simples (« caisse », « écart de caisse »), des montants en FCFA, rien d'interprété.</summary>
public static class EveningReportMessage
{
    public static EmailMessage Render(string commerceName, DayReport report, IReadOnlyDictionary<Guid, string> names)
    {
        var lines = new List<string> { "Bonjour,", string.Empty, $"Voici la journée du {Date(report.Day)} de « {commerceName} ».", string.Empty };
        lines.Add($"VENTES : {report.SalesCount} ticket{(report.SalesCount > 1 ? "s" : string.Empty)}, {Money.Fcfa(report.NetTotalFcfa)} (panier moyen {Money.Fcfa(report.AverageBasketFcfa)}).");
        if (report.Payments.Count > 0)
        {
            lines.Add("PAIEMENTS : " + string.Join(", ", report.Payments.Select(payment => $"{Method(payment.Method)} {Money.Fcfa(payment.AmountFcfa)} ({payment.Count})")) + ".");
            var mobile = report.Payments.Where(payment => payment.Method is not (PaymentMethod.Cash or PaymentMethod.Credit)).ToArray();
            if (mobile.Length > 0)
            {
                lines.Add("À retrouver sur votre téléphone (cet argent n'est pas dans le tiroir) : " + string.Join(", ", mobile.Select(payment => $"{Method(payment.Method)} {Money.Fcfa(payment.AmountFcfa)}")) + ".");
            }
        }

        foreach (var session in report.Sessions.Where(session => session.NeedsClosing))
        {
            lines.Insert(4, $"⚠ À CLÔTURER : la caisse de {Name(names, session.CashierId)} est ouverte depuis le {session.OpenedAt.ToString("dd/MM 'à' HH:mm", CultureInfo.InvariantCulture)}.");
        }

        if (report.Cashiers.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("PAR CAISSIER :");
            foreach (var cashier in report.Cashiers)
            {
                lines.Add($"- {Name(names, cashier.CashierId)} : {cashier.SalesCount} vente{(cashier.SalesCount > 1 ? "s" : string.Empty)}, {Money.Fcfa(cashier.NetTotalFcfa)}"
                    + $" · remises {cashier.DiscountsCount} ({Money.Fcfa(cashier.DiscountFcfa)})"
                    + $" · annulations {cashier.CancellationsCount} ({Money.Fcfa(cashier.CancelledFcfa)})"
                    + $" · retours {cashier.ReturnsCount} ({Money.Fcfa(cashier.ReturnedFcfa)})"
                    + $" · écart de caisse : {Variance(cashier)}");
            }
        }

        if (report.Movements.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("SORTIES ET ENTRÉES D'ESPÈCES :");
            foreach (var movement in report.Movements)
            {
                lines.Add($"- {Kind(movement.Kind)}{(movement.Reason is null ? string.Empty : $" ({movement.Reason})")} : {Money.Fcfa(movement.AmountFcfa)}{(movement.Count > 1 ? $" en {movement.Count} fois" : string.Empty)}");
            }
        }

        if (report.Sessions.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("CAISSES :");
            foreach (var session in report.Sessions)
            {
                lines.Add($"- {Name(names, session.CashierId)} : " + (session.Status == SessionStatus.Open
                    ? $"encore ouverte (depuis {session.OpenedAt.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)})" + (session.DrawerAboveWarning ? " · beaucoup d'argent dans le tiroir : faites un versement au coffre" : string.Empty)
                    : $"clôturée à {session.ClosedAt?.ToString("HH:mm", CultureInfo.InvariantCulture)}, {Money.Fcfa(session.CountedCashFcfa ?? 0)} comptés, écart : {Gap(session.VarianceFcfa ?? 0)}"));
            }
        }

        if (report.DebtCreatedFcfa > 0 || report.RepaymentsFcfa > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"CRÉDIT : dettes créées {Money.Fcfa(report.DebtCreatedFcfa)}, versements encaissés {Money.Fcfa(report.RepaymentsFcfa)}.");
        }

        if (report.LowStock.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("STOCK À SURVEILLER : " + string.Join(" ; ", report.LowStock.Select(item => $"{item.Name} : {Quantity(item)} (seuil {Quantity(item with { Quantity = item.Threshold })})")) + ".");
        }

        lines.Add(string.Empty);
        lines.Add("Ces chiffres sont des faits à regarder : Sôrô n'en tire aucune retenue ni sanction.");
        lines.Add(string.Empty);
        lines.Add("L'équipe Sôrô");
        return new EmailMessage($"Votre journée du {Date(report.Day)} — {commerceName} : {Money.Fcfa(report.NetTotalFcfa)}", string.Join('\n', lines));
    }

    private static string Date(DateOnly day) => day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Name(IReadOnlyDictionary<Guid, string> names, Guid id) => names.GetValueOrDefault(id, "Un caissier");

    private static string Gap(long variance) => variance == 0 ? "aucun" : variance < 0 ? $"manque de {Money.Fcfa(-variance)}" : $"excédent de {Money.Fcfa(variance)}";

    private static string Variance(CashierDay cashier) => cashier.VarianceSessions == 0 && cashier.VarianceFcfa == 0 ? "aucun" : Gap(cashier.VarianceFcfa);

    private static string Kind(CashMovementKind kind) => kind switch
    {
        CashMovementKind.Expense => "Dépenses",
        CashMovementKind.Drop => "Versements au coffre",
        _ => "Apports de monnaie",
    };

    private static string Quantity(LowStockLine item) => item.Unit == MeasureUnit.Piece ? item.Quantity.ToString(CultureInfo.InvariantCulture) : $"{item.Quantity / 1000m:0.###} {item.Unit.Symbol()}";

    private static string Method(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Espèces",
        PaymentMethod.Credit => "Crédit",
        PaymentMethod.OrangeMoney => "Orange Money",
        PaymentMethod.MtnMomo => "MTN MoMo",
        PaymentMethod.MoovMoney => "Moov Money",
        _ => "Wave",
    };
}

/// <summary>
/// L'e-mail du soir (docs/specs/cloture-sorties-rapport CLO-08) : un par commerce actif et par jour, au propriétaire, s'il y a eu de l'activité. Un commerce
/// en lecture seule n'en reçoit pas. Un serveur d'envoi en panne ne bloque rien : le jour n'est pas noté comme envoyé, la prochaine exécution réessaie.
/// </summary>
public sealed class SendEveningReportsHandler(
    IPlatformSubscriptions platform,
    IReportingQueries queries,
    IShopSettings settings,
    IUserRepository users,
    IEveningReportLog log,
    IEmailSender email,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<int>> HandleAsync(DateOnly? day = null, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var date = day ?? DateOnly.FromDateTime(now.UtcDateTime);
        var (from, to) = DayReportBuilder.Bounds(date);
        var sent = 0;
        foreach (var commerce in await platform.ListCommercesAsync(cancellationToken))
        {
            if (commerce.SubscriptionStateAt(now).IsReadOnly || await log.WasSentAsync(commerce.Id, date, cancellationToken))
            {
                continue;
            }

            var shop = await settings.GetAsync(commerce.Id, cancellationToken);
            var figures = await queries.GetDayFiguresAsync(commerce.Id, from, to, cancellationToken);
            var report = DayReportBuilder.Build(figures, date, shop, now, includeStock: true);
            if (!report.HasActivity)
            {
                continue;
            }

            var names = (await users.ListAsync(commerce.Id, cancellationToken)).ToDictionary(user => user.Id.Value, user => user.DisplayName);
            if (!await SubscriptionMessages.TrySendAsync(email, commerce.OwnerEmail, EveningReportMessage.Render(commerce.Name, report, names), cancellationToken))
            {
                continue;
            }

            await log.RecordSentAsync(commerce.Id, date, now, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            sent++;
        }

        return Result<int>.Success(sent);
    }
}
