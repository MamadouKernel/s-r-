using Soro.Domain.Common;

namespace Soro.Domain.Tenancy;

/// <summary>
/// Un commerce client, racine de l'isolation (le <see cref="TenantId"/> de toutes les autres lignes). Créé à l'inscription,
/// avec un essai gratuit dont seule la date de fin est enregistrée ici (la mise en lecture seule RG-14 viendra avec l'abonnement).
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
        DateTimeOffset termsAcceptedAt)
    {
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
        DateTimeOffset termsAcceptedAt) =>
        new(id, name, activity, ownerEmail, formula, createdAt, trialEndsAt, termsVersion, termsAcceptedAt);
}
