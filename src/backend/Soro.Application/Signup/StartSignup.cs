using System.Text.Json;
using Soro.Application.Common;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Tenancy;

namespace Soro.Application.Signup;

public sealed record StartSignupCommand(
    string Phone,
    string CommerceName,
    ActivityType Activity,
    string OwnerName,
    string TermsVersion);

public sealed record SignupStarted(Guid VerificationId, DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt);

/// <summary>Informations saisies à l'inscription, gardées dans la demande jusqu'à la finalisation. Le mot de passe n'en fait pas partie.</summary>
internal sealed record SignupPayload(string CommerceName, ActivityType Activity, string OwnerName, string TermsVersion);

/// <summary>
/// Première étape de l'inscription (INS-01, INS-02, INS-07) : valide les informations, vérifie que le numéro est libre et qu'il n'a pas
/// épuisé son plafond quotidien, puis envoie un code par SMS. <b>Rien n'est créé</b> : ni commerce, ni utilisateur.
/// </summary>
public sealed class StartSignupHandler(
    IOwnerDirectory directory,
    IPhoneVerificationRepository verifications,
    IVerificationCodes codes,
    ISmsSender sms,
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

        PhoneNumber phone;
        try
        {
            phone = PhoneNumber.Parse(command.Phone);
            // Les mêmes règles que la création définitive, vérifiées avant d'envoyer le moindre SMS.
            Commerce.Open(TenantId.New(), command.CommerceName, command.Activity, phone, settings.CurrentTermsVersion, now, settings.TrialDays);
            User.RegisterOwner(UserId.New(), TenantId.New(), phone, command.OwnerName, "validation", now);
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

        if (await directory.FindAsync(phone, cancellationToken) is not null)
        {
            return Result<SignupStarted>.Failure(new ApplicationError(
                ErrorCodes.PhoneAlreadyRegistered, "Ce numéro a déjà un compte : connectez-vous avec votre mot de passe."));
        }

        var started = await verifications.CountStartedSinceAsync(phone, VerificationPurpose.Signup, now.AddDays(-1), cancellationToken);
        if (started >= MaxStartsPerDay)
        {
            return Result<SignupStarted>.Failure(new ApplicationError(
                ErrorCodes.TooManyRequests, "Trop de demandes pour ce numéro aujourd'hui. Réessayez demain."));
        }

        var code = codes.Generate();
        var id = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(new SignupPayload(
            command.CommerceName.Trim(), command.Activity, command.OwnerName.Trim(), settings.CurrentTermsVersion));
        var verification = PhoneVerification.Start(id, VerificationPurpose.Signup, phone, codes.Hash(id, code), payload, now);

        await verifications.AddAsync(verification, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        await sms.SendAsync(phone, SignupMessages.Verification(code), cancellationToken);

        return Result<SignupStarted>.Success(new SignupStarted(id, verification.ExpiresAt, verification.ResendAvailableAt));
    }
}
