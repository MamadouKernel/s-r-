using Microsoft.EntityFrameworkCore;
using Soro.Application.Common;

namespace Soro.Infrastructure.Persistence;

public sealed class AuditTrail(SoroDbContext db) : IAuditTrail
{
    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        db.AuditEntries.Add(new AuditRow
        {
            Id = Guid.CreateVersion7(),
            TenantId = entry.Tenant.Value,
            UserId = entry.User.Value,
            Action = entry.Action,
            ResourceId = entry.ResourceId,
            Detail = entry.Detail,
            At = entry.At.ToUniversalTime(),
        });
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string action, Guid resourceId, CancellationToken cancellationToken) =>
        db.AuditEntries.AnyAsync(row => row.Action == action && row.ResourceId == resourceId, cancellationToken);
}
