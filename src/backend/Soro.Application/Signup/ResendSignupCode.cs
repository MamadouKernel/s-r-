using Soro.Application.Common;
using Soro.Domain.Tenancy;

namespace Soro.Application.Signup;

public sealed record ResendSignupCodeCommand(Guid VerificationId);

/// <summary>Renvoi du code (INS-02) : au plus 3 fois, pas avant 60 secondes ; le nouveau code remplace l'ancien.</summary>
public sealed class ResendSignupCodeHandler(
    IPhoneVerificationRepository verifications,
    IVerificationCodes codes,
    ISmsSender sms,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<SignupStarted>> HandleAsync(
        ResendSignupCodeCommand command,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var verification = await verifications.GetAsync(command.VerificationId, cancellationToken);
        if (verification is null || verification.Purpose != VerificationPurpose.Signup
            || verification.IsConsumed || verification.IsExpired(now))
        {
            return Result<SignupStarted>.Failure(Expired);
        }

        if (verification.ResendCount >= PhoneVerification.MaxResends)
        {
            return Result<SignupStarted>.Failure(new ApplicationError(
                ErrorCodes.TooManyRequests, "Nombre maximal de renvois atteint : recommencez l'inscription."));
        }

        if (!verification.CanResend(now))
        {
            var wait = Math.Max((int)Math.Ceiling((verification.ResendAvailableAt - now).TotalSeconds), 1);
            return Result<SignupStarted>.Failure(new ApplicationError(
                ErrorCodes.TooManyRequests, $"Patientez {wait} seconde{(wait > 1 ? "s" : string.Empty)} avant de demander un nouveau code."));
        }

        var code = codes.Generate();
        verification.Reissue(codes.Hash(verification.Id, code), now);
        await verifications.UpdateAsync(verification, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        await sms.SendAsync(verification.Phone, SignupMessages.Verification(code), cancellationToken);

        return Result<SignupStarted>.Success(
            new SignupStarted(verification.Id, verification.ExpiresAt, verification.ResendAvailableAt));
    }

    internal static ApplicationError Expired { get; } =
        new(ErrorCodes.VerificationExpired, "Cette demande a expiré : recommencez l'inscription.");
}
