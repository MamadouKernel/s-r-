using Microsoft.EntityFrameworkCore;
using Soro.Application.Customers;
using Soro.Domain.Common;
using Soro.Domain.Customers;
using Soro.Domain.Sales;

namespace Soro.Infrastructure.Persistence;

public sealed class CustomerRepository(SoroDbContext db) : ICustomerRepository
{
    public async Task<Customer?> GetAsync(CustomerId id, TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.Customers.SingleOrDefaultAsync(item => item.Id == id.Value && item.TenantId == tenant.Value, cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<Customer?> FindByPhoneAsync(TenantId tenant, PhoneNumber phone, CancellationToken cancellationToken)
    {
        var row = await db.Customers.AsNoTracking().SingleOrDefaultAsync(
            item => item.TenantId == tenant.Value && item.Phone == phone.Value, cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<Customer>> ListAsync(TenantId tenant, string? search, bool includeInactive, CancellationToken cancellationToken)
    {
        var query = db.Customers.AsNoTracking().Where(item => item.TenantId == tenant.Value);
        if (!includeInactive)
        {
            query = query.Where(item => item.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim();
            var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
            var pattern = $"%{Escape(text)}%";
            query = digits.Length == 0
                ? query.Where(item => EF.Functions.ILike(item.Name, pattern, "\\"))
                : query.Where(item => EF.Functions.ILike(item.Name, pattern, "\\") || (item.Phone != null && item.Phone.Contains(digits)));
        }

        var rows = await query.OrderBy(item => item.Name).ToListAsync(cancellationToken);
        return rows.Select(Map).ToArray();
    }

    public Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        db.Customers.Add(new CustomerRow
        {
            Id = customer.Id.Value,
            TenantId = customer.Tenant.Value,
            Name = customer.Name,
            Phone = customer.Phone?.Value,
            CreditCeiling = customer.CreditCeiling.Amount,
            Debt = customer.Debt.Amount,
            CreatedAt = customer.CreatedAt.ToUniversalTime(),
            IsActive = customer.IsActive,
        });
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(Customer customer, CancellationToken cancellationToken)
    {
        var row = await db.Customers.FindAsync([customer.Id.Value], cancellationToken)
            ?? throw new InvalidOperationException($"Le client {customer.Id.Value} n'existe pas.");
        row.Name = customer.Name;
        row.Phone = customer.Phone?.Value;
        row.CreditCeiling = customer.CreditCeiling.Amount;
        row.Debt = customer.Debt.Amount;
        row.IsActive = customer.IsActive;
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static Customer Map(CustomerRow row) =>
        Customer.Restore(
            new CustomerId(row.Id),
            new TenantId(row.TenantId),
            row.Name,
            row.Phone is null ? null : PhoneNumber.Parse(row.Phone),
            Money.Fcfa(row.CreditCeiling),
            Money.Fcfa(row.Debt),
            row.CreatedAt,
            row.IsActive);
}

public sealed class DebtRepaymentRepository(SoroDbContext db) : IDebtRepaymentRepository
{
    public Task<bool> ExistsAsync(DebtRepaymentId id, TenantId tenant, CancellationToken cancellationToken) =>
        db.DebtRepayments.AnyAsync(item => item.Id == id.Value && item.TenantId == tenant.Value, cancellationToken);

    public Task AddAsync(DebtRepayment repayment, CancellationToken cancellationToken)
    {
        db.DebtRepayments.Add(new DebtRepaymentRow
        {
            Id = repayment.Id.Value,
            TenantId = repayment.Tenant.Value,
            CustomerId = repayment.Customer.Value,
            Amount = repayment.Amount.Amount,
            Method = repayment.Method,
            Reference = repayment.Reference,
            ReferenceKey = repayment.Reference is null ? null : SaleMapper.ReferenceKey(repayment.Reference),
            UserId = repayment.User.Value,
            At = repayment.At.ToUniversalTime(),
        });
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<DebtRepayment>> ListForCustomerAsync(
        CustomerId customer,
        TenantId tenant,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await db.DebtRepayments.AsNoTracking()
            .Where(item => item.CustomerId == customer.Value && item.TenantId == tenant.Value)
            .OrderByDescending(item => item.At)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => DebtRepayment.Restore(
                new DebtRepaymentId(row.Id), new TenantId(row.TenantId), new CustomerId(row.CustomerId), Money.Fcfa(row.Amount),
                row.Method, row.Reference, new UserId(row.UserId), row.At))
            .ToArray();
    }

    public Task<bool> IsReferenceUsedAsync(TenantId tenant, PaymentMethod method, string reference, CancellationToken cancellationToken)
    {
        var key = SaleMapper.ReferenceKey(reference);
        return db.DebtRepayments.AnyAsync(
            item => item.TenantId == tenant.Value && item.Method == method && item.ReferenceKey == key, cancellationToken);
    }
}
