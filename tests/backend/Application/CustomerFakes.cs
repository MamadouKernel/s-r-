using Soro.Application.Customers;
using Soro.Domain.Common;
using Soro.Domain.Customers;
using Soro.Domain.Sales;

namespace Soro.Application.Tests;

internal sealed class InMemoryCustomerRepository : ICustomerRepository
{
    private readonly Dictionary<CustomerId, Customer> _customers = [];

    public IEnumerable<Customer> All => _customers.Values;

    public void Put(Customer customer) => _customers[customer.Id] = customer;

    public Task<Customer?> GetAsync(CustomerId id, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_customers.TryGetValue(id, out var customer) && customer.Tenant == tenant ? customer : null);

    public Task<Customer?> FindByPhoneAsync(TenantId tenant, PhoneNumber phone, CancellationToken cancellationToken) =>
        Task.FromResult(_customers.Values.FirstOrDefault(customer => customer.Tenant == tenant && customer.Phone == phone));

    public Task<IReadOnlyList<Customer>> ListAsync(TenantId tenant, string? search, bool includeInactive, CancellationToken cancellationToken)
    {
        var digits = new string((search ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        IReadOnlyList<Customer> found = _customers.Values
            .Where(customer => customer.Tenant == tenant && (includeInactive || customer.IsActive))
            .Where(customer => string.IsNullOrEmpty(search)
                || customer.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || (digits.Length > 0 && customer.Phone is { } phone && phone.Value.Contains(digits, StringComparison.Ordinal)))
            .OrderBy(customer => customer.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return Task.FromResult(found);
    }

    public Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        _customers[customer.Id] = customer;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Customer customer, CancellationToken cancellationToken)
    {
        _customers[customer.Id] = customer;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryDebtRepaymentRepository : IDebtRepaymentRepository
{
    private readonly List<DebtRepayment> _repayments = [];

    public IReadOnlyList<DebtRepayment> All => _repayments;

    public Task<bool> ExistsAsync(DebtRepaymentId id, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_repayments.Any(item => item.Id == id && item.Tenant == tenant));

    public Task AddAsync(DebtRepayment repayment, CancellationToken cancellationToken)
    {
        _repayments.Add(repayment);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DebtRepayment>> ListForCustomerAsync(CustomerId customer, TenantId tenant, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DebtRepayment>>(
            _repayments.Where(item => item.Customer == customer && item.Tenant == tenant).OrderByDescending(item => item.At).Take(limit).ToArray());

    public Task<bool> IsReferenceUsedAsync(TenantId tenant, PaymentMethod method, string reference, CancellationToken cancellationToken) =>
        Task.FromResult(_repayments.Any(item =>
            item.Tenant == tenant && item.Method == method && string.Equals(item.Reference, reference, StringComparison.OrdinalIgnoreCase)));
}
