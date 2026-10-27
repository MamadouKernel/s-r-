using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Application.Sales;

public interface ISaleReturnRepository
{
    Task<SaleReturn?> GetAsync(SaleReturnId id, TenantId tenant, CancellationToken cancellationToken);

    /// <summary>Tous les retours d'une vente, du plus ancien au plus récent.</summary>
    Task<IReadOnlyList<SaleReturn>> ListForSaleAsync(SaleId sale, TenantId tenant, CancellationToken cancellationToken);

    Task AddAsync(SaleReturn saleReturn, CancellationToken cancellationToken);
}
