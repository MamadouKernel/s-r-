using Microsoft.EntityFrameworkCore;
using Soro.Application.Reporting;
using Soro.Domain.Common;

namespace Soro.Infrastructure.Persistence;

/// <summary>Lecture seule du journal : plus récent d'abord, filtré par période et catégorie, toujours pour le commerce du jeton (RG-15).</summary>
public sealed class AuditLogQueries(SoroDbContext db) : IAuditLogQueries
{
    public async Task<AuditPage> ListAsync(TenantId tenant, AuditLogFilter filter, CancellationToken cancellationToken)
    {
        var query = db.AuditEntries.AsNoTracking().Where(entry => entry.TenantId == tenant.Value);
        if (filter.From is { } from)
        {
            query = query.Where(entry => entry.At >= from.ToUniversalTime());
        }

        if (filter.To is { } to)
        {
            query = query.Where(entry => entry.At < to.ToUniversalTime());
        }

        if (filter.ActionPrefixes is { Count: > 0 } prefixes)
        {
            var list = prefixes.ToArray();
            query = query.Where(entry => list.Any(prefix => entry.Action.StartsWith(prefix)));
        }

        // On lit une entrée de plus que demandé pour savoir s'il y a une suite, sans compter toute la table.
        var rows = await query
            .OrderByDescending(entry => entry.At)
            .ThenByDescending(entry => entry.Id)
            .Skip(filter.Offset)
            .Take(filter.Limit + 1)
            .ToListAsync(cancellationToken);

        var entries = rows
            .Take(filter.Limit)
            .Select(row => new AuditEntryView(row.Id, row.At, row.UserId, row.Action, row.ResourceId, row.Detail))
            .ToArray();
        return new AuditPage(entries, rows.Count > filter.Limit);
    }
}
