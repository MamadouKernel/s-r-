using Soro.Domain.Cash;
using Soro.Domain.Common;

namespace Soro.Application.Cash;

/// <summary>Toutes les lectures reçoivent le tenant : l'isolation est appliquée à la source (RG-15).</summary>
public interface ISessionRepository
{
    Task<CashRegisterSession?> GetAsync(SessionId id, TenantId tenant, CancellationToken cancellationToken);

    Task<CashRegisterSession?> FindOpenForRegisterAsync(
        RegisterId registerId,
        TenantId tenant,
        CancellationToken cancellationToken);

    Task AddAsync(CashRegisterSession session, CancellationToken cancellationToken);

    Task UpdateAsync(CashRegisterSession session, CancellationToken cancellationToken);
}
