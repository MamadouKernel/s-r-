using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Application.Signup;

public sealed record StartPasswordResetCommand(string Email);

/// <summary>
/// Mot de passe oublié, première étape (INS-06) : la réponse est la même que l'adresse ait un compte ou non, pour ne pas révéler
/// quels propriétaires existent. Seule une adresse mal écrite est signalé, ce qui ne dit rien d'un compte. 3 demandes par jour au plus, en silence au-delà.
/// </summary>
public sealed class StartPasswordResetHandler(
    IOwnerDirectory directory,
    IEmailVerificationRepository verifications,
    IVerificationCodes codes,
    IEmailSender mail,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<bool>> HandleAsync(
        StartPasswordResetCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!EmailAddress.TryParse(command.Email, out var email))
        {
            return Result<bool>.Failure(ApplicationError.RuleViolation(
                "L'adresse e-mail n'est pas valide : vérifiez-la, par exemple nom@exemple.ci."));
        }

        var now = clock.UtcNow;
        if (await directory.FindAsync(email, cancellationToken) is null)
        {
            return Result<bool>.Success(true);
        }

        var started = await verifications.CountStartedSinceAsync(email, VerificationPurpose.PasswordReset, now.AddDays(-1), cancellationToken);
        if (started >= StartSignupHandler.MaxStartsPerDay)
        {
            return Result<bool>.Success(true);
        }

        var code = codes.Generate();
        var id = Guid.CreateVersion7();
        await verifications.AddAsync(
            EmailVerification.Start(id, VerificationPurpose.PasswordReset, email, codes.Hash(id, code), null, now), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        // Un envoi raté ne doit pas se voir : la réponse serait différente pour un compte existant (SC-I9). Le détail est dans les journaux.
        await StartSignupHandler.TrySendAsync(mail, email, SignupMessages.PasswordReset(code), cancellationToken);
        return Result<bool>.Success(true);
    }
}

public sealed record ConfirmPasswordResetCommand(string Email, string Code, string NewPassword);

/// <summary>
/// Mot de passe oublié, seconde étape (INS-06) : un code exact et un mot de passe accepté changent le mot de passe, lèvent le verrouillage
/// et sont tracés. Toute erreur de code donne le même message, qu'il y ait ou non une demande en cours pour cette adresse.
/// </summary>
public sealed class ConfirmPasswordResetHandler(
    ITenantScope scope,
    IOwnerDirectory directory,
    IEmailVerificationRepository verifications,
    IVerificationCodes codes,
    IUserRepository users,
    IPasswordHasher passwords,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    private static readonly ApplicationError WrongCode =
        new(ErrorCodes.InvalidCode, "Code incorrect ou expiré. Redemandez un code si besoin.");

    public async Task<Result<bool>> HandleAsync(
        ConfirmPasswordResetCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!EmailAddress.TryParse(command.Email, out var email))
        {
            return Result<bool>.Failure(WrongCode);
        }

        var now = clock.UtcNow;
        var verification = await verifications.FindLatestPendingAsync(email, VerificationPurpose.PasswordReset, cancellationToken);
        if (verification is null || !verification.IsUsable(now))
        {
            return Result<bool>.Failure(WrongCode);
        }

        if (!codes.Verify(verification.Id, command.Code ?? string.Empty, verification.CodeHash))
        {
            verification.RecordFailedAttempt();
            await verifications.UpdateAsync(verification, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<bool>.Failure(WrongCode);
        }

        if (PasswordPolicy.Check(command.NewPassword, email) is { } weak)
        {
            return Result<bool>.Failure(ApplicationError.RuleViolation(weak));
        }

        var account = await directory.FindAsync(email, cancellationToken);
        if (account is null)
        {
            return Result<bool>.Failure(WrongCode);
        }

        scope.Enter(account.Tenant);
        var user = await users.GetAsync(account.User, account.Tenant, cancellationToken);
        if (user is null)
        {
            return Result<bool>.Failure(WrongCode);
        }

        try
        {
            user.ChangePassword(passwords.Hash(command.NewPassword), now);
            verification.Consume(now);
        }
        catch (DomainException exception)
        {
            return Result<bool>.Failure(ApplicationError.RuleViolation(exception.Message));
        }

        await users.UpdateAsync(user, cancellationToken);
        await verifications.UpdateAsync(verification, cancellationToken);
        await audit.RecordAsync(
            new AuditEntry(account.Tenant, user.Id, AuditActions.PasswordReset, user.Id.Value, "Mot de passe réinitialisé par e-mail", now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
