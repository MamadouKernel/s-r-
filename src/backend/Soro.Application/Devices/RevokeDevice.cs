using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;

namespace Soro.Application.Devices;

public sealed record RevokeDeviceCommand(DeviceId Device);

/// <summary>Révocation (INS-09) : propriétaire et gérant. Idempotente ; effective à la requête suivante de cet appareil (<see cref="CheckDeviceAccessHandler"/>).</summary>
public sealed class RevokeDeviceHandler(
    ICurrentUser actor,
    IDeviceRepository devices,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<DeviceId>> HandleAsync(RevokeDeviceCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.IsManager(actor.Role))
        {
            return Result<DeviceId>.Failure(ApplicationError.Forbidden("Seuls le propriétaire et le gérant peuvent retirer un appareil."));
        }

        var device = await devices.GetAsync(command.Device, actor.Tenant, cancellationToken);
        if (device is null)
        {
            return Result<DeviceId>.Failure(ApplicationError.NotFound("Appareil introuvable."));
        }

        if (!device.IsActive)
        {
            return Result<DeviceId>.Success(device.Id);
        }

        var now = clock.UtcNow;
        device.Revoke(actor.User, now);
        await devices.UpdateAsync(device, cancellationToken);
        await audit.RecordAsync(
            new AuditEntry(actor.Tenant, actor.User, AuditActions.DeviceRevoked, device.Id.Value, device.Name, now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<DeviceId>.Success(device.Id);
    }
}
