using Microsoft.EntityFrameworkCore;
using Soro.Application.Cash;
using Soro.Domain.Cash;
using Soro.Domain.Common;

namespace Soro.Infrastructure.Persistence;

public sealed class SessionRepository(SoroDbContext db) : ISessionRepository
{
    public async Task<CashRegisterSession?> GetAsync(
        SessionId id,
        TenantId tenant,
        CancellationToken cancellationToken)
    {
        var row = await db.Sessions.SingleOrDefaultAsync(
            session => session.Id == id.Value && session.TenantId == tenant.Value,
            cancellationToken);
        return row is null ? null : SessionMapper.ToDomain(row);
    }

    public async Task<CashRegisterSession?> FindOpenForRegisterAsync(
        RegisterId registerId,
        TenantId tenant,
        CancellationToken cancellationToken)
    {
        var row = await db.Sessions.FirstOrDefaultAsync(
            session => session.TenantId == tenant.Value
                && session.RegisterId == registerId.Value
                && session.Status == SessionStatus.Open,
            cancellationToken);
        return row is null ? null : SessionMapper.ToDomain(row);
    }

    public Task AddAsync(CashRegisterSession session, CancellationToken cancellationToken)
    {
        db.Sessions.Add(SessionMapper.ToRow(session));
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(CashRegisterSession session, CancellationToken cancellationToken)
    {
        var row = await db.Sessions.FindAsync([session.Id.Value], cancellationToken)
            ?? throw new InvalidOperationException($"La session {session.Id.Value} n'existe pas.");
        SessionMapper.Apply(row, session);
    }
}
