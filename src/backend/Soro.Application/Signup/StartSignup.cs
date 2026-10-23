using System.Text.Json;
using Soro.Application.Common;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Tenancy;

namespace Soro.Application.Signup;

public sealed record StartSignupCommand(
    string Email,
    string CommerceName,
    ActivityType Activity,
    string OwnerName,
    string TermsVersion);

public sealed record SignupStarted(Guid VerificationId, DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt);

/// <summary>Informations saisies à l'inscription, gardées dans la demande jusqu'à la finalisation. Le mot de passe n'en fait pas partie.</summary>
internal sealed record SignupPayload(string CommerceName, ActivityType Activity, string OwnerName, string TermsVersion);

/// <summary>
/// Première étape de l'inscription (INS-01, INS-02, INS-07) : valide les informations, vérifie que l'adresse est libre et qu'il n'a pas
/// épuisé son plafond quotidien, puis envoie un code par e-mail. <b>Rien n'est créé</b> : ni commerce, ni utilisateur.
/// </summary>
public sealed class StartSignupHandler(
    IOwnerDirectory directory,
    IEmailVerificationRepository verifications,
    IVerificationCodes codes,
    IEmailSender mail,
    ISignupSettings settings,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public const int MaxStartsPerDay = 3;

    public async Task<Result<SignupStarted>> HandleAsync(
        StartSignupCommand command,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        EmailAddress email;
        try
        {
            email = EmailAddress.Parse(command.Email);
            // Les mêmes règles que la création définitive, vérifiées avant d'envoyer le moindre e-mail.
            Commerce.Open(TenantId.New(), command.CommerceName, command.Activity, email, settings.CurrentTermsVersion, now, settings.TrialDays);
            User.RegisterOwner(UserId.New(), TenantId.New(), email, command.OwnerName, "validation", now);
        }
        catch (DomainException exception)
        {
            return Result<SignupStarted>.Failure(ApplicationError.RuleViolation(exception.Message));
        }

        if (!string.Equals(command.TermsVersion?.Trim(), settings.CurrentTermsVersion, StringComparison.Ordinal))
        {
            return Result<SignupStarted>.Failure(
                ApplicationError.RuleViolation("Vous devez accepter les conditions d'utilisation en vigueur pour vous inscrire."));
        }

        if (await directory.FindAsync(email, cancellationToken) is not null)
        {
            return Result<SignupStarted>.Failure(new ApplicationError(
                ErrorCodes.EmailAlreadyRegistered, "Cette adresse e-mail a déjà un compte : connectez-vous avec votre mot de passe."));
        }

        var started = await verifications.CountStartedSinceAsync(email, VerificationPurpose.Signup, now.AddDays(-1), cancellationToken);
        if (started >= MaxStartsPerDay)
        {
            return Result<SignupStarted>.Failure(new ApplicationError(
                ErrorCodes.TooManyRequests, "Trop de demandes pour cette adresse aujourd'hui. Réessayez demain."));
        }

        var code = codes.Generate();
        var id = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(new SignupPayload(
            command.CommerceName.Trim(), command.Activity, command.OwnerName.Trim(), settings.CurrentTermsVersion));
        var verification = EmailVerification.Start(id, VerificationPurpose.Signup, email, codes.Hash(id, code), payload, now);

        await verifications.AddAsync(verification, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        if (!await TrySendAsync(mail, email, SignupMessages.Verification(code), cancellationToken))
        {
            return Result<SignupStarted>.Failure(NotSent);
        }

        return Result<SignupStarted>.Success(new SignupStarted(id, verification.ExpiresAt, verification.ResendAvailableAt));
    }

    internal static ApplicationError NotSent { get; } = new(
        ErrorCodes.EmailNotSent,
        "Nous n'avons pas pu envoyer l'e-mail. Vérifiez l'adresse ou réessayez dans quelques minutes.");

    internal static async Task<bool> TrySendAsync(
        IEmailSender mail,
        EmailAddress recipient,
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            await mail.SendAsync(recipient, message, cancellationToken);
            return true;
        }
        catch (EmailDeliveryException)
        {
            return false;
        }
    }
}
