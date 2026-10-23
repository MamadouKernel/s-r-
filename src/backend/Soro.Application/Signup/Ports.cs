using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Application.Signup;

/// <summary>Un e-mail à envoyer : un objet court et un texte simple, sans mise en forme.</summary>
public sealed record EmailMessage(string Subject, string Body);

/// <summary>Envoi d'un e-mail (ADR 0005). En production : SMTP via MailKit ; en développement : un adaptateur qui écrit le message dans le journal.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailAddress recipient, EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Le message n'a pas pu partir (serveur d'envoi indisponible, adresse refusée…). Le détail est dans les journaux, jamais dans la réponse.</summary>
public sealed class EmailDeliveryException(Exception inner) : Exception("L'e-mail n'a pas pu être envoyé.", inner);

/// <summary>Codes de vérification : tirage aléatoire sûr, condensat lié à la demande, comparaison en temps constant.</summary>
public interface IVerificationCodes
{
    /// <summary>Un code à 6 chiffres, tiré avec un générateur cryptographique.</summary>
    string Generate();

    string Hash(Guid verificationId, string code);

    bool Verify(Guid verificationId, string code, string expectedHash);
}

public interface IEmailVerificationRepository
{
    Task<EmailVerification?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>La demande la plus récente de cette adresse et de cet usage, non consommée (mot de passe oublié : on connaît l'adresse, pas la demande).</summary>
    Task<EmailVerification?> FindLatestPendingAsync(
        EmailAddress email,
        VerificationPurpose purpose,
        CancellationToken cancellationToken);

    /// <summary>Nombre de demandes de cette adresse et de cet usage créées depuis <paramref name="since"/> (plafond quotidien).</summary>
    Task<int> CountStartedSinceAsync(
        EmailAddress email,
        VerificationPurpose purpose,
        DateTimeOffset since,
        CancellationToken cancellationToken);

    Task AddAsync(EmailVerification verification, CancellationToken cancellationToken);

    Task UpdateAsync(EmailVerification verification, CancellationToken cancellationToken);
}

/// <summary>Un compte propriétaire : l'adresse mène au commerce et à l'utilisateur, sans que le commerce soit connu d'avance.</summary>
public sealed record OwnerAccount(EmailAddress Email, TenantId Tenant, UserId User);

/// <summary>Annuaire des propriétaires, hors isolation par commerce : il ne contient aucun secret, seulement adresse → commerce.</summary>
public interface IOwnerDirectory
{
    Task<OwnerAccount?> FindAsync(EmailAddress email, CancellationToken cancellationToken);

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
