using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Signup;
using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Application.Devices;

public sealed record EnrollDeviceCommand(string Token, string? DeviceName);

/// <summary>Ce que l'appareil garde : son identifiant et son secret (rendu une seule fois), le commerce auquel il appartient.</summary>
public sealed record DeviceEnrolled(DeviceId Device, string Secret, TenantId Tenant, string CommerceName, string DeviceName);

/// <summary>
/// Enrôlement (INS-09) : consomme l'invitation et crée l'appareil. Un jeton falsifié ne reçoit aucun détail ; seul celui qui présente le bon
/// secret apprend que son lien est déjà utilisé ou expiré, avec des mots simples.
/// </summary>
public sealed class EnrollDeviceHandler(
    ITenantScope scope,
    IDeviceInvitationRepository invitations,
    IDeviceRepository devices,
    ICommerceRepository commerces,
    IDeviceSecrets secrets,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public const string DefaultName = "Caisse";

    public async Task<Result<DeviceEnrolled>> HandleAsync(
        EnrollDeviceCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!EnrollmentToken.TryParse(command.Token, out var tenant, out var invitationId, out var secret))
        {
            secrets.Verify(Guid.Empty, command.Token ?? string.Empty, null);
            return Result<DeviceEnrolled>.Failure(Invalid);
        }

        scope.Enter(tenant);
        var invitation = await invitations.GetAsync(invitationId, tenant, cancellationToken);
        if (invitation is null || !secrets.Verify(invitation.Id.Value, secret, invitation.SecretHash))
        {
            if (invitation is null)
            {
                secrets.Verify(Guid.Empty, secret, null);
            }

            return Result<DeviceEnrolled>.Failure(Invalid);
        }

        var now = clock.UtcNow;
        if (invitation.IsConsumed)
        {
            return Result<DeviceEnrolled>.Failure(new ApplicationError(
                ErrorCodes.InvalidInvitation, "Ce lien a déjà été utilisé. Demandez-en un nouveau au propriétaire."));
        }

        if (invitation.IsExpired(now))
        {
            return Result<DeviceEnrolled>.Failure(new ApplicationError(
                ErrorCodes.InvalidInvitation, "Ce lien a expiré. Demandez-en un nouveau au propriétaire."));
        }

        var commerce = await commerces.GetAsync(tenant, cancellationToken);
        if (commerce is null)
        {
            return Result<DeviceEnrolled>.Failure(Invalid);
        }

        var deviceId = DeviceId.New();
        var deviceSecret = secrets.NewSecret();
        Device device;
        try
        {
            device = Device.Enroll(
                deviceId,
                tenant,
                string.IsNullOrWhiteSpace(command.DeviceName) ? invitation.SuggestedName ?? DefaultName : command.DeviceName,
                secrets.Hash(deviceId.Value, deviceSecret),
                invitation.Id,
                now);
            invitation.Consume(now);
        }
        catch (DomainException exception)
        {
            return Result<DeviceEnrolled>.Failure(ApplicationError.RuleViolation(exception.Message));
        }

        await devices.AddAsync(device, cancellationToken);
        await invitations.UpdateAsync(invitation, cancellationToken);
        await audit.RecordAsync(
            new AuditEntry(tenant, invitation.CreatedBy, AuditActions.DeviceEnrolled, deviceId.Value, device.Name, now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result<DeviceEnrolled>.Success(new DeviceEnrolled(deviceId, deviceSecret, tenant, commerce.Name, device.Name));
    }

    public static ApplicationError Invalid { get; } = new(
        ErrorCodes.InvalidInvitation, "Ce lien n'est pas valide. Demandez-en un nouveau au propriétaire.");
}
