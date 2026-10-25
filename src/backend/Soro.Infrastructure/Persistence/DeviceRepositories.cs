using Microsoft.EntityFrameworkCore;
using Soro.Application.Devices;
using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Infrastructure.Persistence;

public sealed class DeviceRepository(SoroDbContext db) : IDeviceRepository
{
    public async Task<Device?> GetAsync(DeviceId id, TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.Devices.SingleOrDefaultAsync(item => item.Id == id.Value && item.TenantId == tenant.Value, cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<Device>> ListAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        var rows = await db.Devices.AsNoTracking().Where(item => item.TenantId == tenant.Value).ToListAsync(cancellationToken);
        return rows.Select(Map).ToArray();
    }

    public Task AddAsync(Device device, CancellationToken cancellationToken)
    {
        db.Devices.Add(new DeviceRow
        {
            Id = device.Id.Value,
            TenantId = device.Tenant.Value,
            Name = device.Name,
            SecretHash = device.SecretHash,
            InvitationId = device.Invitation.Value,
            EnrolledAt = device.EnrolledAt.ToUniversalTime(),
            LastSeenAt = device.LastSeenAt.ToUniversalTime(),
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reporte seulement ce qui change. EF n'écrit que les colonnes dont la valeur diffère de celle lue : un « dernière activité » qui
    /// arrive après une révocation concurrente n'efface donc pas la révocation.
    /// </summary>
    public async Task UpdateAsync(Device device, CancellationToken cancellationToken)
    {
        var row = await db.Devices.FindAsync([device.Id.Value], cancellationToken)
            ?? throw new InvalidOperationException($"L'appareil {device.Id.Value} n'existe pas.");
        row.LastSeenAt = device.LastSeenAt.ToUniversalTime();
        if (device.RevokedAt is not null)
        {
            row.RevokedAt = device.RevokedAt.Value.ToUniversalTime();
            row.RevokedBy = device.RevokedBy?.Value;
        }
    }

    private static Device Map(DeviceRow row) =>
        Device.Restore(
            new DeviceId(row.Id),
            new TenantId(row.TenantId),
            row.Name,
            row.SecretHash,
            new DeviceInvitationId(row.InvitationId),
            row.EnrolledAt,
            row.LastSeenAt,
            row.RevokedAt,
            row.RevokedBy is { } by ? new UserId(by) : null);
}

public sealed class DeviceInvitationRepository(SoroDbContext db) : IDeviceInvitationRepository
{
    public async Task<DeviceInvitation?> GetAsync(DeviceInvitationId id, TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.DeviceInvitations.SingleOrDefaultAsync(
            item => item.Id == id.Value && item.TenantId == tenant.Value, cancellationToken);
        return row is null
            ? null
            : DeviceInvitation.Restore(
                new DeviceInvitationId(row.Id),
                new TenantId(row.TenantId),
                row.SecretHash,
                new UserId(row.CreatedBy),
                row.SuggestedName,
                row.CreatedAt,
                row.ConsumedAt,
                row.RevokedAt);
    }

    public async Task<IReadOnlyList<DeviceInvitation>> ListPendingAsync(TenantId tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var instant = now.ToUniversalTime();
        var rows = await db.DeviceInvitations
            .AsNoTracking()
            .Where(item => item.TenantId == tenant.Value && item.ConsumedAt == null && item.RevokedAt == null && item.ExpiresAt > instant)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => DeviceInvitation.Restore(
                new DeviceInvitationId(row.Id),
                new TenantId(row.TenantId),
                row.SecretHash,
                new UserId(row.CreatedBy),
                row.SuggestedName,
                row.CreatedAt,
                row.ConsumedAt,
                row.RevokedAt))
            .ToArray();
    }

    public Task AddAsync(DeviceInvitation invitation, CancellationToken cancellationToken)
    {
        db.DeviceInvitations.Add(new DeviceInvitationRow
        {
            Id = invitation.Id.Value,
            TenantId = invitation.Tenant.Value,
            SecretHash = invitation.SecretHash,
            CreatedBy = invitation.CreatedBy.Value,
            SuggestedName = invitation.SuggestedName,
            CreatedAt = invitation.CreatedAt.ToUniversalTime(),
            ExpiresAt = invitation.ExpiresAt.ToUniversalTime(),
        });
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(DeviceInvitation invitation, CancellationToken cancellationToken)
    {
        var row = await db.DeviceInvitations.FindAsync([invitation.Id.Value], cancellationToken)
            ?? throw new InvalidOperationException($"L'invitation {invitation.Id.Value} n'existe pas.");
        row.ConsumedAt = invitation.ConsumedAt?.ToUniversalTime();
        row.RevokedAt = invitation.RevokedAt?.ToUniversalTime();
    }
}
