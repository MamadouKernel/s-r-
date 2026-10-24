using Soro.Application.Common;
using Soro.Application.Devices;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Identity;

public sealed record LogInCommand(TenantId Tenant, string Identifier, string Pin, DeviceCredentials? Device = null);

public sealed record LogInResult(AccessToken Token, UserId UserId, string DisplayName, Role Role);

/// <summary>
/// Connexion par identifiant et code PIN. Une seule réponse (« identifiant ou code incorrect ») pour un compte
/// inconnu, désactivé ou un mauvais PIN, afin de ne pas révéler quels comptes existent. Seul un compte verrouillé
/// reçoit une réponse distincte. Les échecs sont comptés et validés même quand la connexion échoue.
/// L'appareil est contrôlé <b>avant</b> le compte (ADR 0006) : un appareil refusé ne peut ni deviner un PIN ni verrouiller un compte.
/// </summary>
public sealed class LogInHandler(
    ITenantScope scope,
    IUserRepository users,
    IDeviceRepository devices,
    IDeviceSecrets deviceSecrets,
    IDevicePolicy devicePolicy,
    IPinHasher hasher,
    ITokenIssuer tokens,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    private static readonly ApplicationError Refused =
        new(ErrorCodes.InvalidCredentials, "Identifiant ou code PIN incorrect.");

    private static readonly ApplicationError Locked =
        new(ErrorCodes.AccountLocked, "Compte verrouillé temporairement. Réessayez dans quelques minutes.");

    private static readonly ApplicationError DeviceRefused = new(
        ErrorCodes.DeviceNotAuthorized,
        "Cet appareil n'est plus autorisé. Demandez un nouveau lien d'accès au propriétaire.");

    public async Task<Result<LogInResult>> HandleAsync(
        LogInCommand command,
        CancellationToken cancellationToken = default)
    {
        scope.Enter(command.Tenant);
        var now = clock.UtcNow;

        DeviceId? device = null;
        if (command.Device is { } credentials)
        {
            var known = await devices.GetAsync(credentials.Device, command.Tenant, cancellationToken);
            var secretMatches = deviceSecrets.Verify(credentials.Device.Value, credentials.Secret ?? string.Empty, known?.SecretHash);
            if (known is null || !secretMatches || !known.IsActive)
            {
                return Result<LogInResult>.Failure(DeviceRefused);
            }

            device = known.Id;
        }
        else if (devicePolicy.Required)
        {
            return Result<LogInResult>.Failure(DeviceRefused);
        }

        var user = await users.FindByIdentifierAsync(
            command.Tenant, User.Normalize(command.Identifier), cancellationToken);
        if (user is null || !PinPolicy.IsValid(command.Pin))
        {
            hasher.Verify(command.Pin ?? string.Empty, null);
            return Result<LogInResult>.Failure(Refused);
        }

        if (user.IsLocked(now))
        {
            return Result<LogInResult>.Failure(Locked);
        }

        var pinMatches = hasher.Verify(command.Pin, user.PinHash);
        if (user.Status != UserStatus.Active)
        {
            return Result<LogInResult>.Failure(Refused);
        }

        if (!pinMatches)
        {
            user.RecordFailedAttempt(now);
            await users.UpdateAsync(user, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<LogInResult>.Failure(Refused);
        }

        user.RecordSuccessfulLogin(now);
        await users.UpdateAsync(user, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result<LogInResult>.Success(new LogInResult(
            tokens.Issue(user.Tenant, user.Id, user.Role, now, device), user.Id, user.DisplayName, user.Role));
    }
}
