using Microsoft.EntityFrameworkCore;
using Soro.Application.Cash;
using Soro.Domain.Cash;
using Soro.Domain.Common;

namespace Soro.Infrastructure.Persistence;

public sealed class CashMovementRepository(SoroDbContext db) : ICashMovementRepository
{
    public async Task<CashMovement?> GetAsync(CashMovementId id, TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.CashMovements.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id.Value && item.TenantId == tenant.Value, cancellationToken);
        return row is null
            ? null
            : CashMovement.Restore(
                new CashMovementId(row.Id), new TenantId(row.TenantId), new SessionId(row.SessionId), row.Kind, Money.Fcfa(row.Amount), row.Reason, row.At,
                new UserId(row.ByUserId), row.ApprovedBy is { } approver ? new UserId(approver) : null);
    }

    public Task AddAsync(CashMovement movement, CancellationToken cancellationToken)
    {
        db.CashMovements.Add(new CashMovementRow
        {
            Id = movement.Id.Value,
            TenantId = movement.Tenant.Value,
            SessionId = movement.Session.Value,
            Kind = movement.Kind,
            Amount = movement.Amount.Amount,
            Reason = movement.Reason,
            At = movement.At.ToUniversalTime(),
            ByUserId = movement.By.Value,
            ApprovedBy = movement.ApprovedBy?.Value,
        });
        return Task.CompletedTask;
    }
}
