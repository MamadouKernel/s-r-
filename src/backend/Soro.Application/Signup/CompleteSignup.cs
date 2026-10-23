using System.Text.Json;
using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Tenancy;

namespace Soro.Application.Signup;

public sealed record CompleteSignupCommand(Guid VerificationId, string Code, string Password);

public sealed record CompleteSignupResult(
    AccessToken Token,
    UserId UserId,
    string DisplayName,
    TenantId Tenant,
    string CommerceName,
    DateTimeOffset TrialEndsAt);

/// <summary>
/// Seconde étape de l'inscription (INS-03) : vérifie le code, puis crée <b>en une seule transaction</b> le commerce, le propriétaire et son
/// entrée d'annuaire, trace la création et connecte le propriétaire. Un mot de passe refusé ne consomme pas le code ; un code faux compte un essai.
/// </summary>
public sealed class CompleteSignupHandler(
    ITenantScope scope,
    IPhoneVerificationRepository verifications,
    IVerificationCodes codes,
    IOwnerDirectory directory,
    ICommerceRepository commerces,
    IUserRepository users,
    IPasswordHasher passwords,
    IAuditTrail audit,
    ITokenIssuer tokens,
    ISignupSettings settings,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<CompleteSignupResult>> HandleAsync(
        CompleteSignupCommand command,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var verification = await verifications.GetAsync(command.VerificationId, cancellationToken);
        if (verification is null || verification.Purpose != VerificationPurpose.Signup || !verification.IsUsable(now))
        {
            return Result<CompleteSignupResult>.Failure(ResendSignupCodeHandler.Expired);
        }

        if (!codes.Verify(verification.Id, command.Code ?? string.Empty, verification.CodeHash))
        {
            verification.RecordFailedAttempt();
            await verifications.UpdateAsync(verification, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<CompleteSignupResult>.Failure(verification.AttemptsLeft > 0
                ? new ApplicationError(
                    ErrorCodes.InvalidCode,
                    $"Code incorrect. Il vous reste {verification.AttemptsLeft} essai{(verification.AttemptsLeft > 1 ? "s" : string.Empty)}.")
                : new ApplicationError(ErrorCodes.VerificationExpired, "Trop d'essais : recommencez l'inscription."));
        }

        if (PasswordPolicy.Check(command.Password, verification.Phone) is { } weak)
        {
            // Le code est exact : on ne le consomme pas, le commerçant peut choisir un autre mot de passe.
            return Result<CompleteSignupResult>.Failure(ApplicationError.RuleViolation(weak));
        }

        SignupPayload? payload = null;
        if (verification.Payload is not null)
        {
            try
            {
                payload = JsonSerializer.Deserialize<SignupPayload>(verification.Payload);
            }
            catch (JsonException)
            {
                // Demande illisible : traitée comme expirée plutôt qu'une erreur interne.
            }
        }

        if (payload is null)
        {
            return Result<CompleteSignupResult>.Failure(ResendSignupCodeHandler.Expired);
        }

        if (await directory.FindAsync(verification.Phone, cancellationToken) is not null)
        {
            return Result<CompleteSignupResult>.Failure(new ApplicationError(
                ErrorCodes.PhoneAlreadyRegistered, "Ce numéro a déjà un compte : connectez-vous avec votre mot de passe."));
        }

        try
        {
            var tenant = TenantId.New();
            scope.Enter(tenant);
            var commerce = Commerce.Open(
                tenant, payload.CommerceName, payload.Activity, verification.Phone, payload.TermsVersion, now, settings.TrialDays);
            var owner = User.RegisterOwner(UserId.New(), tenant, verification.Phone, payload.OwnerName, passwords.Hash(command.Password), now);
            owner.RecordSuccessfulLogin(now);

            await commerces.AddAsync(commerce, cancellationToken);
            await users.AddAsync(owner, cancellationToken);
            await directory.AddAsync(new OwnerAccount(verification.Phone, tenant, owner.Id), cancellationToken);
            await audit.RecordAsync(
                new AuditEntry(tenant, owner.Id, AuditActions.CommerceCreated, tenant.Value, commerce.Name, now), cancellationToken);
            verification.Consume(now);
            await verifications.UpdateAsync(verification, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return Result<CompleteSignupResult>.Success(new CompleteSignupResult(
                tokens.Issue(tenant, owner.Id, Role.Owner, now), owner.Id, owner.DisplayName, tenant, commerce.Name, commerce.TrialEndsAt));
        }
        catch (DomainException exception)
        {
            return Result<CompleteSignupResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}
