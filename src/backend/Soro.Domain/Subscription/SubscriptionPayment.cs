using Soro.Domain.Common;

namespace Soro.Domain.Subscription;

/// <summary>Moyen de paiement de l'abonnement : Mobile Money seulement pour l'instant (docs/decisions/0011).</summary>
public enum SubscriptionMethod
{
    Wave,
    OrangeMoney,
    MtnMomo,
    MoovMoney,
}

public enum SubscriptionPaymentStatus
{
    Pending,
    Validated,
    Refused,
}

/// <summary>
/// Un paiement d'abonnement déclaré par le propriétaire, puis validé ou refusé par l'équipe Sôrô après vérification sur son relevé.
/// Tant qu'il est en attente, il n'active rien.
/// </summary>
public sealed class SubscriptionPayment
{
    private const int MinReferenceKeyLength = 3;
    private const int MaxReferenceLength = 100;
    private const int MaxReasonLength = 500;

    private SubscriptionPayment(
        SubscriptionPaymentId id,
        TenantId tenant,
        SubscriptionPeriod period,
        SubscriptionMethod method,
        string reference,
        string referenceKey,
        long amountFcfa,
        DateTimeOffset declaredAt)
    {
        Id = id;
        Tenant = tenant;
        Period = period;
        Method = method;
        Reference = reference;
        ReferenceKey = referenceKey;
        AmountFcfa = amountFcfa;
        DeclaredAt = declaredAt;
    }

    public SubscriptionPaymentId Id { get; }

    public TenantId Tenant { get; }

    public SubscriptionPeriod Period { get; }

    public SubscriptionMethod Method { get; }

    /// <summary>Référence telle que le propriétaire l'a saisie.</summary>
    public string Reference { get; }

    /// <summary>Forme de comparaison : sans casse, espaces ni ponctuation (« W-123 456 » et « w123456 » sont la même).</summary>
    public string ReferenceKey { get; }

    public long AmountFcfa { get; }

    public DateTimeOffset DeclaredAt { get; }

    public SubscriptionPaymentStatus Status { get; private set; } = SubscriptionPaymentStatus.Pending;

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Motif d'un refus.</summary>
    public string? Reason { get; private set; }

    public DateTimeOffset? CoveredFrom { get; private set; }

    public DateTimeOffset? CoveredTo { get; private set; }

    public static SubscriptionPayment Declare(
        SubscriptionPaymentId id,
        TenantId tenant,
        SubscriptionPeriod period,
        SubscriptionMethod method,
        string reference,
        long amountFcfa,
        DateTimeOffset declaredAt)
    {
        var trimmed = (reference ?? string.Empty).Trim();
        var key = KeyOf(trimmed);
        Ensure.That(trimmed.Length > 0, "La référence de la transaction est obligatoire.");
        Ensure.That(key.Length >= MinReferenceKeyLength, "La référence de la transaction est trop courte.");
        Ensure.That(trimmed.Length <= MaxReferenceLength, $"La référence ne peut pas dépasser {MaxReferenceLength} caractères.");
        Ensure.That(amountFcfa > 0, "Le montant doit être strictement positif.");
        return new SubscriptionPayment(id, tenant, period, method, trimmed, key, amountFcfa, declaredAt);
    }

    public static SubscriptionPayment Restore(
        SubscriptionPaymentId id,
        TenantId tenant,
        SubscriptionPeriod period,
        SubscriptionMethod method,
        string reference,
        string referenceKey,
        long amountFcfa,
        DateTimeOffset declaredAt,
        SubscriptionPaymentStatus status,
        DateTimeOffset? decidedAt,
        string? reason,
        DateTimeOffset? coveredFrom,
        DateTimeOffset? coveredTo) =>
        new(id, tenant, period, method, reference, referenceKey, amountFcfa, declaredAt)
        {
            Status = status,
            DecidedAt = decidedAt,
            Reason = reason,
            CoveredFrom = coveredFrom,
            CoveredTo = coveredTo,
        };

    public static string KeyOf(string reference) =>
        new(reference.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Valide le paiement : il couvre la période à partir de <paramref name="coveredFrom"/>.</summary>
    public void Validate(DateTimeOffset coveredFrom, DateTimeOffset at)
    {
        EnsurePending();
        Status = SubscriptionPaymentStatus.Validated;
        CoveredFrom = coveredFrom;
        CoveredTo = Period.AddTo(coveredFrom);
        DecidedAt = at;
    }

    public void Refuse(string reason, DateTimeOffset at)
    {
        EnsurePending();
        Ensure.That(!string.IsNullOrWhiteSpace(reason), "Le motif du refus est obligatoire.");
        Ensure.That(reason.Trim().Length <= MaxReasonLength, $"Le motif ne peut pas dépasser {MaxReasonLength} caractères.");
        Status = SubscriptionPaymentStatus.Refused;
        Reason = reason.Trim();
        DecidedAt = at;
    }

    private void EnsurePending() => Ensure.That(Status == SubscriptionPaymentStatus.Pending, "Ce paiement a déjà été traité.");
}
