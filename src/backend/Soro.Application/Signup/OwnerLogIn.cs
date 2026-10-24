using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Signup;

public sealed record OwnerLogInCommand(string Email, string Password);

public sealed record OwnerLogInResult(AccessToken Token, UserId UserId, string DisplayName, TenantId Tenant, string CommerceName);

/// <summary>
/// Connexion du propriétaire par adresse e-mail et mot de passe (INS-05). Une seule réponse pour une adresse inconnue, mal écrit, un compte désactivé ou
/// un mauvais mot de passe, avec le même travail de calcul ; seul un compte verrouillé reçoit une réponse distincte (comme pour le PIN, ADR 0003).
/// </summary>
public sealed class OwnerLogInHandler(
    ITenantScope scope,
    IOwnerDirectory directory,
    ICommerceRepository commerces,
    IUserRepository users,
    IPasswordHasher passwords,
    ITokenIssuer tokens,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    private static readonly ApplicationError Refused =
        new(ErrorCodes.InvalidCredentials, "Adresse e-mail ou mot de passe incorrect.");

    private static readonly ApplicationError Locked =
        new(ErrorCodes.AccountLocked, "Compte verrouillé temporairement. Réessayez dans quelques minutes.");

    public async Task<Result<OwnerLogInResult>> HandleAsync(
        OwnerLogInCommand command,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var password = command.Password ?? string.Empty;

        if (!EmailAddress.TryParse(command.Email, out var email))
        {
            return Refuse(password);
        }

        var account = await directory.FindAsync(email, cancellationToken);
        if (account is null)
        {
            return Refuse(password);
        }

        scope.Enter(account.Tenant);
        var user = await users.GetAsync(account.User, account.Tenant, cancellationToken);
        if (user?.PasswordHash is null)
        {
            return Refuse(password);
        }

        if (user.IsLocked(now))
        {
            return Result<OwnerLogInResult>.Failure(Locked);
        }

        var matches = passwords.Verify(password, user.PasswordHash);
        if (user.Status != UserStatus.Active)
        {
            return Result<OwnerLogInResult>.Failure(Refused);
        }

        if (!matches)
        {
            user.RecordFailedAttempt(now);
            await users.UpdateAsync(user, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<OwnerLogInResult>.Failure(Refused);
        }

        user.RecordSuccessfulLogin(now);
        await users.UpdateAsync(user, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var commerce = await commerces.GetAsync(account.Tenant, cancellationToken);
        return Result<OwnerLogInResult>.Success(new OwnerLogInResult(
            tokens.Issue(user.Tenant, user.Id, user.Role, now), user.Id, user.DisplayName, user.Tenant, commerce?.Name ?? string.Empty));
    }

    /// <summary>Même travail de calcul qu'un compte réel : on ne devine pas l'existence d'une adresse au temps de réponse.</summary>
    private Result<OwnerLogInResult> Refuse(string password)
    {
        passwords.Verify(password, null);
        return Result<OwnerLogInResult>.Failure(Refused);
    }
}
