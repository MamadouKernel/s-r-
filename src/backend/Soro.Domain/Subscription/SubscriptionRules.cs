namespace Soro.Domain.Subscription;

public enum SubscriptionStatus
{
    /// <summary>Essai gratuit : rien n'a encore été payé.</summary>
    Trial,

    /// <summary>Une période payée est en cours.</summary>
    Active,

    /// <summary>L'échéance est passée ; tout fonctionne encore, un bandeau prévient.</summary>
    Grace,

    /// <summary>Délai de grâce écoulé : plus de nouvelle opération en ligne ; les données sont conservées (RG-14).</summary>
    ReadOnly,
}

/// <param name="EndsAt">Fin de l'essai ou de la dernière période payée.</param>
/// <param name="ReadOnlyFrom">Instant où la grâce prend fin.</param>
/// <param name="DaysLeft">Jours avant l'échéance (essai, abonnement) ou avant la lecture seule (grâce) ; 0 en lecture seule. Un jour entamé compte.</param>
public sealed record SubscriptionState(SubscriptionStatus Status, DateTimeOffset EndsAt, DateTimeOffset ReadOnlyFrom, int DaysLeft)
{
    public bool IsReadOnly => Status == SubscriptionStatus.ReadOnly;
}

public enum SubscriptionPeriod
{
    Monthly,
    Annual,
}

public static class SubscriptionPeriods
{
    public static int Months(this SubscriptionPeriod period) => period == SubscriptionPeriod.Annual ? 12 : 1;

    public static DateTimeOffset AddTo(this SubscriptionPeriod period, DateTimeOffset from) => from.AddMonths(period.Months());
}

/// <summary>États d'abonnement dérivés de dates, jamais stockés : rien à synchroniser, aucune tâche qui puisse l'oublier (docs/decisions/0011).</summary>
public static class SubscriptionRules
{
    public const int GraceDays = 7;

    /// <summary>Après le début de la lecture seule, les ventes faites hors ligne sont encore acceptées pendant ce délai ; au-delà, il faut payer d'abord.</summary>
    public const int OfflineQueueDays = 7;

    public static DateTimeOffset CurrentEnd(DateTimeOffset trialEndsAt, DateTimeOffset? paidUntil) =>
        paidUntil is { } paid && paid > trialEndsAt ? paid : trialEndsAt;

    public static SubscriptionState StateAt(DateTimeOffset trialEndsAt, DateTimeOffset? paidUntil, DateTimeOffset now, int graceDays = GraceDays)
    {
        var end = CurrentEnd(trialEndsAt, paidUntil);
        var readOnlyFrom = end.AddDays(graceDays);
        if (now < end)
        {
            return new(paidUntil is null ? SubscriptionStatus.Trial : SubscriptionStatus.Active, end, readOnlyFrom, DaysUntil(end, now));
        }

        return now < readOnlyFrom
            ? new(SubscriptionStatus.Grace, end, readOnlyFrom, DaysUntil(readOnlyFrom, now))
            : new(SubscriptionStatus.ReadOnly, end, readOnlyFrom, 0);
    }

    /// <summary>
    /// Début de la période qu'un paiement validé à <paramref name="now"/> couvrirait : à la suite de la période en cours (les jours d'essai ou
    /// déjà payés restent acquis), ou aujourd'hui si elle est finie (aucun jour offert).
    /// </summary>
    public static DateTimeOffset CoverageStart(DateTimeOffset trialEndsAt, DateTimeOffset? paidUntil, DateTimeOffset now)
    {
        var end = CurrentEnd(trialEndsAt, paidUntil);
        return end > now ? end : now;
    }

    private static int DaysUntil(DateTimeOffset instant, DateTimeOffset now) => (int)Math.Ceiling((instant - now).TotalDays);
}
