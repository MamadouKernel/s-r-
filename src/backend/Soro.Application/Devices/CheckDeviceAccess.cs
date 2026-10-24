using Soro.Application.Common;
using Soro.Domain.Common;

namespace Soro.Application.Devices;

/// <summary>
/// Contrôle fait à chaque requête portant un jeton d'appareil (ADR 0006) : l'appareil existe-t-il encore, dans ce commerce, et n'est-il pas révoqué ?
/// Note aussi sa dernière activité, au plus toutes les 5 minutes.
/// </summary>
public sealed class CheckDeviceAccessHandler(IDeviceRepository devices, IClock clock, IUnitOfWork unitOfWork)
{
    public async Task<bool> IsAllowedAsync(DeviceId deviceId, TenantId tenant, CancellationToken cancellationToken = default)
    {
        var device = await devices.GetAsync(deviceId, tenant, cancellationToken);
        if (device is null || !device.IsActive)
        {
            return false;
        }

        if (device.Touch(clock.UtcNow))
        {
            await devices.UpdateAsync(device, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return true;
    }
}
