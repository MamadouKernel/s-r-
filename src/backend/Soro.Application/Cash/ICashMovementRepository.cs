using Soro.Domain.Cash;
using Soro.Domain.Common;

namespace Soro.Application.Cash;

/// <summary>Les sorties et entrées d'espèces. Jamais de modification ni de suppression : seulement lire et ajouter.</summary>
public interface ICashMovementRepository
{
    Task<CashMovement?> GetAsync(CashMovementId id, TenantId tenant, CancellationToken cancellationToken);

    Task AddAsync(CashMovement movement, CancellationToken cancellationToken);
}
