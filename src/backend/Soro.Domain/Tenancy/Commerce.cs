using Soro.Domain.Common;
using Soro.Domain.Subscription;

namespace Soro.Domain.Tenancy;

/// <summary>
/// Un commerce client, racine de l'isolation (le <see cref="TenantId"/> de toutes les autres lignes). Créé à l'inscription,
/// avec un essai gratuit ; la fin de la dernière période payée s'ajoute ensuite (docs/decisions/0011). L'état d'abonnement se calcule, il ne se stocke pas.
/// </summary>
public sealed class Commerce
{
    public const string DefaultFormula = "Commerce";
    public const int DefaultTrialDays = 30;

    private const int MinNameLength = 2;
    private const int MaxNameLength = 100;
    private const int MaxTrialDays = 366;

    private Commerce(
        TenantId id,
        string name,
        ActivityType activity,
        EmailAddress ownerEmail,
        string formula,
        DateTimeOffset createdAt,
        DateTimeOffset trialEndsAt,
        string termsVersion,
        DateTimeOffset termsAcceptedAt,
        DateTimeOffset? paidUntil = null)
    {
        PaidUntil = paidUntil;
        Id = id;
        Name = name;
        Activity = activity;
        OwnerEmail = ownerEmail;
        Formula = formula;
        CreatedAt = createdAt;
        TrialEndsAt = trialEndsAt;
        TermsVersion = termsVersion;
        TermsAcceptedAt = termsAcceptedAt;
    }

    public TenantId Id { get; }

    public string Name { get; }

    public ActivityType Activity { get; }

    public EmailAddress OwnerEmail { get; }

    public string Formula { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset TrialEndsAt { get; }

    /// <summary>Version des conditions d'utilisation acceptées par le propriétaire à l'inscription.</summary>
    public string TermsVersion { get; }

    public DateTimeOffset TermsAcceptedAt { get; }

    /// <summary>Fin de la dernière période payée ; <c>null</c> tant que rien n'a été payé.</summary>
    public DateTimeOffset? PaidUntil { get; private set; }

    public static Commerce Open(
        TenantId id,
        string name,
        ActivityType activity,
        EmailAddress ownerEmail,
        string termsVersion,
        DateTimeOffset now,
        int trialDays = DefaultTrialDays)
    {
        var trimmed = (name ?? string.Empty).Trim();
        Ensure.That(
            trimmed.Length is >= MinNameLength and <= MaxNameLength,
            $"Le nom du commerce doit faire {MinNameLength} à {MaxNameLength} caractères.");
        Ensure.That(!string.IsNullOrWhiteSpace(termsVersion), "L'acceptation des conditions d'utilisation est obligatoire.");
        Ensure.That(trialDays is >= 1 and <= MaxTrialDays, "La durée de l'essai gratuit est invalide.");

        return new Commerce(id, trimmed, activity, ownerEmail, DefaultFormula, now, now.AddDays(trialDays), termsVersion.Trim(), now);
    }

    public bool IsTrialOver(DateTimeOffset now) => now >= TrialEndsAt;

    public SubscriptionState SubscriptionStateAt(DateTimeOffset now) => SubscriptionRules.StateAt(TrialEndsAt, PaidUntil, now);

    /// <summary>Début de la période qu'un paiement validé maintenant couvrirait (RG-14, docs/decisions/0011).</summary>
    public DateTimeOffset CoverageStartAt(DateTimeOffset now) => SubscriptionRules.CoverageStart(TrialEndsAt, PaidUntil, now);

    /// <summary>Un paiement validé prolonge l'abonnement ; la fin de période ne recule jamais.</summary>
    public void ExtendPaidUntil(DateTimeOffset paidUntil)
    {
        Ensure.That(PaidUntil is null || paidUntil > PaidUntil, "La période payée ne peut pas reculer.");
        PaidUntil = paidUntil;
    }

    /// <summary>Reconstruction depuis le stockage : l'état a déjà été validé à l'ouverture.</summary>
    internal static Commerce Restore(
        TenantId id,
        string name,
        ActivityType activity,
        EmailAddress ownerEmail,
        string formula,
        DateTimeOffset createdAt,
        DateTimeOffset trialEndsAt,
        string termsVersion,
        DateTimeOffset termsAcceptedAt,
        DateTimeOffset? paidUntil = null) =>
        new(id, name, activity, ownerEmail, formula, createdAt, trialEndsAt, termsVersion, termsAcceptedAt, paidUntil);
}
