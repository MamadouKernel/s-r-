using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Application.Devices;

public sealed record CreateDeviceInvitationCommand(string? SuggestedName);

public sealed record DeviceInvitationCreated(DeviceInvitationId Id, string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Invitation d'appareil (INS-08) : propriétaire et gérant seulement. Le jeton n'est rendu qu'ici, une seule fois ; la base ne garde que
/// le condensat de son secret.
/// </summary>
public sealed class CreateDeviceInvitationHandler(
    ICurrentUser actor,
    IDeviceInvitationRepository invitations,
    IDeviceSecrets secrets,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<DeviceInvitationCreated>> HandleAsync(
        CreateDeviceInvitationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.IsManager(actor.Role))
        {
            return Result<DeviceInvitationCreated>.Failure(
                ApplicationError.Forbidden("Seuls le propriétaire et le gérant peuvent ajouter un appareil."));
        }

        var now = clock.UtcNow;
        var id = DeviceInvitationId.New();
        var secret = secrets.NewSecret();
        DeviceInvitation invitation;
        try
        {
            invitation = DeviceInvitation.Issue(id, actor.Tenant, secrets.Hash(id.Value, secret), actor.User, command.SuggestedName, now);
        }
        catch (DomainException exception)
        {
            return Result<DeviceInvitationCreated>.Failure(ApplicationError.RuleViolation(exception.Message));
        }

        await invitations.AddAsync(invitation, cancellationToken);
        await audit.RecordAsync(
            new AuditEntry(actor.Tenant, actor.User, AuditActions.DeviceInvited, id.Value, invitation.SuggestedName ?? "Appareil", now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result<DeviceInvitationCreated>.Success(
            new DeviceInvitationCreated(id, EnrollmentToken.Format(actor.Tenant, id, secret), invitation.ExpiresAt));
    }
}
