using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Application.Signup;

/// <summary>Envoi d'un SMS. Le fournisseur réel se choisit avant le pilote (ADR 0005) ; en attendant, un adaptateur de développement.</summary>
public interface ISmsSender
{
    Task SendAsync(PhoneNumber recipient, string message, CancellationToken cancellationToken);
}

/// <summary>Codes de vérification : tirage aléatoire sûr, condensat lié à la demande, comparaison en temps constant.</summary>
public interface IVerificationCodes
{
    /// <summary>Un code à 6 chiffres, tiré avec un générateur cryptographique.</summary>
    string Generate();

    string Hash(Guid verificationId, string code);

    bool Verify(Guid verificationId, string code, string expectedHash);
}

public interface IPhoneVerificationRepository
{
    Task<PhoneVerification?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>La demande la plus récente de ce numéro et de cet usage, non consommée (mot de passe oublié : on connaît le numéro, pas la demande).</summary>
    Task<PhoneVerification?> FindLatestPendingAsync(
        PhoneNumber phone,
        VerificationPurpose purpose,
        CancellationToken cancellationToken);

    /// <summary>Nombre de demandes de ce numéro et de cet usage créées depuis <paramref name="since"/> (plafond quotidien).</summary>
    Task<int> CountStartedSinceAsync(
        PhoneNumber phone,
        VerificationPurpose purpose,
        DateTimeOffset since,
        CancellationToken cancellationToken);

    Task AddAsync(PhoneVerification verification, CancellationToken cancellationToken);

    Task UpdateAsync(PhoneVerification verification, CancellationToken cancellationToken);
}

/// <summary>Un compte propriétaire : le numéro mène au commerce et à l'utilisateur, sans que le commerce soit connu d'avance.</summary>
public sealed record OwnerAccount(PhoneNumber Phone, TenantId Tenant, UserId User);

/// <summary>Annuaire des propriétaires, hors isolation par commerce : il ne contient aucun secret, seulement numéro → commerce.</summary>
public interface IOwnerDirectory
{
    Task<OwnerAccount?> FindAsync(PhoneNumber phone, CancellationToken cancellationToken);

    Task AddAsync(OwnerAccount account, CancellationToken cancellationToken);
}

public interface ICommerceRepository
{
    Task<Commerce?> GetAsync(TenantId tenant, CancellationToken cancellationToken);

    Task AddAsync(Commerce commerce, CancellationToken cancellationToken);
}

/// <summary>Réglages d'inscription : version des conditions d'utilisation en vigueur et durée de l'essai.</summary>
public interface ISignupSettings
{
    string CurrentTermsVersion { get; }

    int TrialDays { get; }
}
