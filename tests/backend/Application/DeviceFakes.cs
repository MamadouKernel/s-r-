using Soro.Application.Devices;
using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Application.Tests;

internal sealed class InMemoryDeviceRepository : IDeviceRepository
{
    private readonly Dictionary<DeviceId, Device> _devices = [];

    public int Count => _devices.Count;

    public int Updates { get; private set; }

    public IEnumerable<Device> All => _devices.Values;

    public Task<Device?> GetAsync(DeviceId id, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_devices.TryGetValue(id, out var device) && device.Tenant == tenant ? device : null);

    public Task<IReadOnlyList<Device>> ListAsync(TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Device>>(_devices.Values.Where(device => device.Tenant == tenant).ToArray());

    public Task AddAsync(Device device, CancellationToken cancellationToken)
    {
        _devices[device.Id] = device;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Device device, CancellationToken cancellationToken)
    {
        Updates++;
        _devices[device.Id] = device;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryDeviceInvitationRepository : IDeviceInvitationRepository
{
    private readonly Dictionary<DeviceInvitationId, DeviceInvitation> _invitations = [];

    public IEnumerable<DeviceInvitation> All => _invitations.Values;

    public Task<DeviceInvitation?> GetAsync(DeviceInvitationId id, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_invitations.TryGetValue(id, out var invitation) && invitation.Tenant == tenant ? invitation : null);

    public Task<IReadOnlyList<DeviceInvitation>> ListPendingAsync(TenantId tenant, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DeviceInvitation>>(
            _invitations.Values.Where(invitation => invitation.Tenant == tenant && invitation.IsPending(now)).ToArray());

    public Task AddAsync(DeviceInvitation invitation, CancellationToken cancellationToken)
    {
        _invitations[invitation.Id] = invitation;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(DeviceInvitation invitation, CancellationToken cancellationToken)
    {
        _invitations[invitation.Id] = invitation;
        return Task.CompletedTask;
    }
}

/// <summary>Secrets prévisibles et condensat lisible (<c>h(propriétaire):secret</c>), pour tester sans hasard.</summary>
internal sealed class FakeDeviceSecrets : IDeviceSecrets
{
    private int _next;

    public int DummyVerifications { get; private set; }

    public string NewSecret() => $"secret{++_next:D3}";

    public string Hash(Guid ownerId, string secret) => $"h({ownerId:N}):{secret}";

    public bool Verify(Guid ownerId, string secret, string? expectedHash)
    {
        if (expectedHash is null)
        {
            DummyVerifications++;
            return false;
        }

        return Hash(ownerId, secret) == expectedHash;
    }
}

internal sealed class FakeDevicePolicy : IDevicePolicy
{
    public bool Required { get; set; }
}
