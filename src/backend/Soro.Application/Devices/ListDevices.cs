using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;

namespace Soro.Application.Devices;

/// <summary>Un appareil tel que l'écran le montre : jamais le condensat de son secret.</summary>
public sealed record DeviceView(
    Guid Id,
    string Name,
    DateTimeOffset EnrolledAt,
    DateTimeOffset LastSeenAt,
    bool IsActive,
    DateTimeOffset? RevokedAt);

/// <summary>Liste des appareils du commerce (INS-09) : propriétaire et gérant, les plus récents d'abord.</summary>
public sealed class ListDevicesHandler(ICurrentUser actor, IDeviceRepository devices)
{
    public async Task<Result<IReadOnlyList<DeviceView>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.IsManager(actor.Role))
        {
            return Result<IReadOnlyList<DeviceView>>.Failure(
                ApplicationError.Forbidden("Seuls le propriétaire et le gérant peuvent voir les appareils."));
        }

        var list = await devices.ListAsync(actor.Tenant, cancellationToken);
        IReadOnlyList<DeviceView> views = list
            .OrderByDescending(device => device.EnrolledAt)
            .Select(device => new DeviceView(
                device.Id.Value, device.Name, device.EnrolledAt, device.LastSeenAt, device.IsActive, device.RevokedAt))
            .ToArray();
        return Result<IReadOnlyList<DeviceView>>.Success(views);
    }
}
