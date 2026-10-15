using Soro.Application.Common;

namespace Soro.Infrastructure.Persistence;

/// <summary>Un seul <c>SaveChanges</c> : vente, session et audit sont écrits dans la même transaction.</summary>
public sealed class EfUnitOfWork(SoroDbContext db) : IUnitOfWork
{
    public async Task CommitAsync(CancellationToken cancellationToken) =>
        await db.SaveChangesAsync(cancellationToken);
}
