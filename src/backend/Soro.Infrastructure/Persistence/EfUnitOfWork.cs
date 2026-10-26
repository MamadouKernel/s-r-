using Soro.Application.Common;

namespace Soro.Infrastructure.Persistence;

/// <summary>Un seul <c>SaveChanges</c> : vente, session et audit sont écrits dans la même transaction.</summary>
public sealed class EfUnitOfWork(SoroDbContext db) : IUnitOfWork
{
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (PersistenceErrors.IsUniqueViolation(exception) || PersistenceErrors.IsConcurrencyConflict(exception))
        {
            // Le contexte garde les objets de l'écriture ratée : sans les oublier, le cas suivant du même lot échouerait de la même façon.
            db.ChangeTracker.Clear();
            throw new ConflictException(exception);
        }
    }
}
