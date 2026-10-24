using Soro.Domain.Common;
using Soro.Domain.Customers;
using Soro.Domain.Sales;

namespace Soro.Application.Customers;

public interface ICustomerRepository
{
    Task<Customer?> GetAsync(CustomerId id, TenantId tenant, CancellationToken cancellationToken);

    /// <summary>Le client qui porte ce numéro (déjà normalisé) dans ce commerce, désactivé ou non : un numéro ne sert qu'à un client.</summary>
    Task<Customer?> FindByPhoneAsync(TenantId tenant, PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>Clients du commerce, filtrés par un texte (nom, ou chiffres du téléphone), triés par nom.</summary>
    Task<IReadOnlyList<Customer>> ListAsync(TenantId tenant, string? search, bool includeInactive, CancellationToken cancellationToken);

    Task AddAsync(Customer customer, CancellationToken cancellationToken);

    Task UpdateAsync(Customer customer, CancellationToken cancellationToken);
}

public interface IDebtRepaymentRepository
{
    Task<bool> ExistsAsync(DebtRepaymentId id, TenantId tenant, CancellationToken cancellationToken);

    Task AddAsync(DebtRepayment repayment, CancellationToken cancellationToken);

    /// <summary>Versements d'un client, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<DebtRepayment>> ListForCustomerAsync(CustomerId customer, TenantId tenant, int limit, CancellationToken cancellationToken);

    /// <summary>Une référence Mobile Money déjà portée par un versement (les ventes ont leur propre contrôle).</summary>
    Task<bool> IsReferenceUsedAsync(TenantId tenant, PaymentMethod method, string reference, CancellationToken cancellationToken);
}
