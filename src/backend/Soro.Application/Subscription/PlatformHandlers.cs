using System.Globalization;
using Soro.Application.Common;
using Soro.Application.Signup;
using Soro.Domain.Common;
using Soro.Domain.Subscription;

namespace Soro.Application.Subscription;

public sealed record PendingSubscriptionPayment(
    Guid Id,
    string CommerceName,
    string OwnerEmail,
    SubscriptionPeriod Period,
    SubscriptionMethod Method,
    string Reference,
    long AmountFcfa,
    DateTimeOffset DeclaredAt);

/// <summary>Paiements en attente de vérification sur le relevé de Sôrô (outil d'exploitation, ABO-05).</summary>
public sealed class ListPendingSubscriptionPaymentsHandler(IPlatformSubscriptions platform)
{
    public async Task<IReadOnlyList<PendingSubscriptionPayment>> HandleAsync(CancellationToken cancellationToken = default) =>
        (await platform.ListPendingAsync(cancellationToken))
            .Select(item => new PendingSubscriptionPayment(
                item.Payment.Id.Value, item.Commerce.Name, item.Commerce.OwnerEmail.Value, item.Payment.Period, item.Payment.Method,
                item.Payment.Reference, item.Payment.AmountFcfa, item.Payment.DeclaredAt))
            .OrderBy(item => item.DeclaredAt)
            .ToArray();
}

public sealed record ValidatedSubscriptionPayment(DateTimeOffset PaidUntil, bool EmailSent);

/// <summary>Valide un paiement : la période payée s'ajoute à la suite de la période en cours, sans jour perdu ni offert (ABO-05).</summary>
public sealed class ValidateSubscriptionPaymentHandler(
    IPlatformSubscriptions platform,
    IEmailSender email,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<ValidatedSubscriptionPayment>> HandleAsync(SubscriptionPaymentId id, CancellationToken cancellationToken = default)
    {
        var found = await platform.FindPaymentAsync(id, cancellationToken);
        if (found is null)
        {
            return Result<ValidatedSubscriptionPayment>.Failure(ApplicationError.NotFound("Paiement introuvable."));
        }

        try
        {
            var now = clock.UtcNow;
            var (payment, commerce) = (found.Payment, found.Commerce);
            payment.Validate(commerce.CoverageStartAt(now), now);
            commerce.ExtendPaidUntil(payment.CoveredTo!.Value);
            await platform.SaveDecisionAsync(payment, commerce, cancellationToken);
            await audit.RecordAsync(
                new AuditEntry(
                    commerce.Id,
                    new Domain.Common.UserId(Guid.Empty),
                    AuditActions.SubscriptionPaymentValidated,
                    payment.Id.Value,
                    $"{payment.Period} · {payment.Method} · {payment.Reference} → jusqu'au {SubscriptionMessages.Date(payment.CoveredTo.Value)}",
                    now),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            var sent = await SubscriptionMessages.TrySendAsync(
                email,
                commerce.OwnerEmail,
                new EmailMessage(
                    "Votre paiement Sôrô est validé",
                    $"Bonjour,\n\nVotre paiement du {SubscriptionMessages.Date(payment.DeclaredAt)} pour « {commerce.Name} » est validé. "
                    + $"Votre abonnement est actif jusqu'au {SubscriptionMessages.Date(payment.CoveredTo.Value)}.\n\nMerci de votre confiance.\nL'équipe Sôrô"),
                cancellationToken);
            return Result<ValidatedSubscriptionPayment>.Success(new(payment.CoveredTo.Value, sent));
        }
        catch (DomainException exception)
        {
            return Result<ValidatedSubscriptionPayment>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}

public sealed record RefuseSubscriptionPaymentCommand(SubscriptionPaymentId Id, string Reason);

public sealed class RefuseSubscriptionPaymentHandler(
    IPlatformSubscriptions platform,
    IEmailSender email,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<bool>> HandleAsync(RefuseSubscriptionPaymentCommand command, CancellationToken cancellationToken = default)
    {
        var found = await platform.FindPaymentAsync(command.Id, cancellationToken);
        if (found is null)
        {
            return Result<bool>.Failure(ApplicationError.NotFound("Paiement introuvable."));
        }

        try
        {
            found.Payment.Refuse(command.Reason, clock.UtcNow);
            await platform.SaveDecisionAsync(found.Payment, found.Commerce, cancellationToken);
            await audit.RecordAsync(
                new AuditEntry(
                    found.Commerce.Id,
                    new Domain.Common.UserId(Guid.Empty),
                    AuditActions.SubscriptionPaymentRefused,
                    found.Payment.Id.Value,
                    $"{found.Payment.Method} · {found.Payment.Reference} : {found.Payment.Reason}",
                    clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            await SubscriptionMessages.TrySendAsync(
                email,
                found.Commerce.OwnerEmail,
                new EmailMessage(
                    "Votre paiement Sôrô n'a pas pu être validé",
                    $"Bonjour,\n\nNous n'avons pas pu valider votre paiement du {SubscriptionMessages.Date(found.Payment.DeclaredAt)} pour « {found.Commerce.Name} ».\n\n"
                    + $"Motif : {found.Payment.Reason}\n\nVous pouvez le déclarer de nouveau dans Sôrô, rubrique « Mon abonnement », avec la bonne référence.\nL'équipe Sôrô"),
                cancellationToken);
            return Result<bool>.Success(true);
        }
        catch (DomainException exception)
        {
            return Result<bool>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}

/// <summary>
/// Rappels d'échéance (ABO-06) : à 7 jours, à 3 jours, au dernier jour (et pendant la grâce), puis un dernier message à l'entrée en lecture seule.
/// Chacun part une seule fois par échéance, même si la tâche tourne plusieurs fois ; un jour manqué est rattrapé. Renvoie le nombre d'e-mails partis.
/// </summary>
public sealed class SendSubscriptionRemindersHandler(
    IPlatformSubscriptions platform,
    IEmailSender email,
    ISubscriptionSettings settings,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<int>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var sent = 0;
        foreach (var commerce in await platform.ListCommercesAsync(cancellationToken))
        {
            var state = commerce.SubscriptionStateAt(now);
            var kind = KindFor(state, now);
            if (kind is null || await platform.WasReminderSentAsync(commerce.Id, state.EndsAt, kind, cancellationToken))
            {
                continue;
            }

            if (!await SubscriptionMessages.TrySendAsync(email, commerce.OwnerEmail, SubscriptionMessages.Reminder(commerce, state, kind, settings), cancellationToken))
            {
                continue; // le serveur d'envoi est en panne : on réessaiera à la prochaine exécution
            }

            await platform.RecordReminderAsync(commerce.Id, state.EndsAt, kind, now, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            sent++;
        }

        return Result<int>.Success(sent);
    }

    private static string? KindFor(SubscriptionState state, DateTimeOffset now)
    {
        if (state.IsReadOnly)
        {
            return SubscriptionMessages.ReadOnlyKind;
        }

        var left = state.EndsAt - now;
        if (state.Status == SubscriptionStatus.Grace || left <= TimeSpan.FromDays(1))
        {
            return SubscriptionMessages.LastDayKind;
        }

        if (left <= TimeSpan.FromDays(3))
        {
            return SubscriptionMessages.ThreeDaysKind;
        }

        return left <= TimeSpan.FromDays(7) ? SubscriptionMessages.SevenDaysKind : null;
    }
}

internal static class SubscriptionMessages
{
    public const string SevenDaysKind = "J-7";
    public const string ThreeDaysKind = "J-3";
    public const string LastDayKind = "J";
    public const string ReadOnlyKind = "lecture-seule";

    public static string Date(DateTimeOffset date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Un e-mail qui ne part pas ne doit jamais défaire une décision déjà enregistrée : on le dit au lieu de lever une erreur.</summary>
    public static async Task<bool> TrySendAsync(IEmailSender email, EmailAddress to, EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await email.SendAsync(to, message, cancellationToken);
            return true;
        }
        catch (EmailDeliveryException)
        {
            return false;
        }
    }

    public static EmailMessage Reminder(Domain.Tenancy.Commerce commerce, SubscriptionState state, string kind, ISubscriptionSettings settings)
    {
        var what = state.Status == SubscriptionStatus.Trial ? "Votre essai gratuit" : "Votre abonnement";
        var how = "Pour continuer sans interruption :\n"
            + $"1. Envoyez {Money.Fcfa(settings.MonthlyPriceFcfa)} pour un mois ({Money.Fcfa(settings.AnnualPriceFcfa)} pour un an) par Mobile Money à l'un de ces numéros :\n"
            + string.Concat(settings.PayNumbers.Select(number => $"   - {Label(number.Key)} : {number.Value}\n"))
            + "2. Notez la référence de la transaction, reçue par message.\n"
            + "3. Dans Sôrô, ouvrez « Mon abonnement » et saisissez cette référence.\n";
        var (subject, intro) = kind switch
        {
            ReadOnlyKind => (
                "Votre boutique Sôrô est en lecture seule",
                $"Le délai de grâce de « {commerce.Name} » est terminé : la boutique est en lecture seule.\n"
                + "Rien n'est perdu : toutes vos données sont conservées et vous pouvez les consulter. Les ventes déjà faites sans connexion seront enregistrées.\n"
                + "Dès que votre paiement est validé, tout reprend."),
            LastDayKind when state.Status == SubscriptionStatus.Grace => (
                "Votre abonnement Sôrô est arrivé à échéance",
                $"{what} de « {commerce.Name} » s'est terminé le {Date(state.EndsAt)}. Tout fonctionne encore jusqu'au {Date(state.ReadOnlyFrom)} ; après cette date, la boutique passera en lecture seule."),
            LastDayKind => (
                "Votre abonnement Sôrô se termine aujourd'hui",
                $"{what} de « {commerce.Name} » se termine le {Date(state.EndsAt)}, dans moins d'un jour."),
            _ => (
                $"Votre abonnement Sôrô se termine dans {state.DaysLeft} jours",
                $"{what} de « {commerce.Name} » se termine dans {state.DaysLeft} jours, le {Date(state.EndsAt)}."),
        };
        return new EmailMessage(subject, $"Bonjour,\n\n{intro}\n\n{how}\nMerci,\nL'équipe Sôrô");
    }

    private static string Label(SubscriptionMethod method) => method switch
    {
        SubscriptionMethod.OrangeMoney => "Orange Money",
        SubscriptionMethod.MtnMomo => "MTN MoMo",
        SubscriptionMethod.MoovMoney => "Moov Money",
        _ => "Wave",
    };
}
