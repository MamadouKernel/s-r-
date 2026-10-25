using Soro.Application.Common;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Identity;

public sealed record DisableUserCommand(UserId UserId);

/// <summary>
/// Désactive un utilisateur (départ d'un caissier). Même règle de droits que la création : on ne désactive que
/// les profils qu'on peut créer. Un compte désactivé ne peut plus se connecter ; ses sessions ouvertes prennent fin.
/// </summary>
public sealed class DisableUserHandler(
    ICurrentUser actor,
    IUserRepository users,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<UserId>> HandleAsync(
        DisableUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var target = await users.GetAsync(command.UserId, actor.Tenant, cancellationToken);
        if (target is null)
        {
            return Result<UserId>.Failure(ApplicationError.NotFound("Utilisateur introuvable."));
        }

        if (!RolePolicy.CanManageRole(actor.Role, target.Role))
        {
            return Result<UserId>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas désactiver cet utilisateur."));
        }

        if (target.Status == UserStatus.Disabled)
        {
            return Result<UserId>.Success(target.Id);
        }

        target.Disable(clock.UtcNow);
        await users.UpdateAsync(target, cancellationToken);
        await audit.RecordAsync(
            new AuditEntry(actor.Tenant, actor.User, AuditActions.UserDisabled, target.Id.Value, target.Identifier, clock.UtcNow),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<UserId>.Success(target.Id);
    }
}
