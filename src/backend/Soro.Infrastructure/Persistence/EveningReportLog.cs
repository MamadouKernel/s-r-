using Microsoft.EntityFrameworkCore;
using Soro.Application.Reporting;
using Soro.Domain.Common;

namespace Soro.Infrastructure.Persistence;

public sealed class EveningReportLog(SoroDbContext db) : IEveningReportLog
{
    public Task<bool> WasSentAsync(TenantId tenant, DateOnly day, CancellationToken cancellationToken) =>
        db.EveningReports.AnyAsync(row => row.TenantId == tenant.Value && row.Day == day, cancellationToken);

    public Task RecordSentAsync(TenantId tenant, DateOnly day, DateTimeOffset at, CancellationToken cancellationToken)
    {
        db.EveningReports.Add(new EveningReportRow { TenantId = tenant.Value, Day = day, SentAt = at.ToUniversalTime() });
        return Task.CompletedTask;
    }
}
