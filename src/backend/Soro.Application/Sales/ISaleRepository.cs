using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Application.Sales;

public interface ISaleRepository
{
    Task<Sale?> GetAsync(SaleId id, TenantId tenant, CancellationToken cancellationToken);

    /// <summary>
    /// Une référence Mobile Money ne sert qu'une fois (RG-04). Périmètre retenu : commerce et opérateur,
    /// à confirmer avec le pilote (OP-2). L'infrastructure doublonne ce contrôle par une contrainte d'unicité.
    /// </summary>
    Task<bool> IsMobileMoneyReferenceUsedAsync(
        TenantId tenant,
        PaymentMethod method,
        string reference,
        CancellationToken cancellationToken);

    Task AddAsync(Sale sale, CancellationToken cancellationToken);

    Task UpdateAsync(Sale sale, CancellationToken cancellationToken);
}
