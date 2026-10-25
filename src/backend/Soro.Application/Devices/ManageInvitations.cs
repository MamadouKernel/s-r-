using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;

namespace Soro.Application.Devices;

/// <summary>Un lien d'accès encore en attente, tel que l'écran Appareils le montre : jamais son secret, ni le lien lui-même.</summary>
public sealed record PendingInvitationView(Guid Id, string Name, string CreatedByName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>Liens d'accès en attente (docs/specs/equipe-gestion-complete EQP-04) : propriétaire et gérant, les plus récents d'abord.</summary>
public sealed class ListDeviceInvitationsHandler(
    ICurrentUser actor,
    IDeviceInvitationRepository invitations,
    IUserRepository users,
    IClock clock)
{
    private const string UnnamedDevice = "Appareil sans nom";
    private const string UnknownAuthor = "Un membre de l'équipe";

    public async Task<Result<IReadOnlyList<PendingInvitationView>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.IsManager(actor.Role))
        {
            return Result<IReadOnlyList<PendingInvitationView>>.Failure(
                ApplicationError.Forbidden("Seuls le propriétaire et le gérant peuvent voir les liens en attente."));
        }

        var pending = await invitations.ListPendingAsync(actor.Tenant, clock.UtcNow, cancellationToken);
        var authors = (await users.ListAsync(actor.Tenant, cancellationToken)).ToDictionary(user => user.Id, user => user.DisplayName);
        IReadOnlyList<PendingInvitationView> views = pending
            .OrderByDescending(invitation => invitation.CreatedAt)
            .Select(invitation => new PendingInvitationView(
                invitation.Id.Value,
                invitation.SuggestedName ?? UnnamedDevice,
                authors.GetValueOrDefault(invitation.CreatedBy, UnknownAuthor),
                invitation.CreatedAt,
                invitation.ExpiresAt))
            .ToArray();
        return Result<IReadOnlyList<PendingInvitationView>>.Success(views);
    }
}

public sealed record RevokeDeviceInvitationCommand(DeviceInvitationId Invitation);

/// <summary>
/// Annulation d'un lien d'accès qui n'a pas servi (EQP-04) : le lien répond ensuite exactement comme un lien périmé. Rejouable ; un lien déjà utilisé
/// renvoie vers la liste des appareils, où l'on retire l'appareil.
/// </summary>
public sealed class RevokeDeviceInvitationHandler(
    ICurrentUser actor,
    IDeviceInvitationRepository invitations,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<DeviceInvitationId>> HandleAsync(
        RevokeDeviceInvitationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.IsManager(actor.Role))
        {
            return Result<DeviceInvitationId>.Failure(
                ApplicationError.Forbidden("Seuls le propriétaire et le gérant peuvent annuler un lien."));
        }

        var invitation = await invitations.GetAsync(command.Invitation, actor.Tenant, cancellationToken);
        if (invitation is null)
        {
            return Result<DeviceInvitationId>.Failure(ApplicationError.NotFound("Lien introuvable."));
        }

        if (invitation.IsRevoked)
        {
            return Result<DeviceInvitationId>.Success(invitation.Id);
        }

        if (invitation.IsConsumed)
        {
            return Result<DeviceInvitationId>.Failure(ApplicationError.RuleViolation(
                "Ce lien a déjà servi : l'appareil figure dans la liste des appareils, où vous pouvez le retirer."));
        }

        var now = clock.UtcNow;
        invitation.Revoke(now);
        await invitations.UpdateAsync(invitation, cancellationToken);
        await audit.RecordAsync(
            new AuditEntry(
                actor.Tenant, actor.User, AuditActions.DeviceInvitationRevoked, invitation.Id.Value, invitation.SuggestedName ?? "Appareil sans nom", now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<DeviceInvitationId>.Success(invitation.Id);
    }
}
