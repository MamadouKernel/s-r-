using Soro.Domain.Customers;

namespace Soro.Application.Customers;

/// <summary>Un client tel que les écrans et la caisse le lisent : la dette et le crédit disponible sont calculés ici, jamais par le client.</summary>
public sealed record CustomerView(
    Guid Id,
    string Name,
    string? Phone,
    long CreditCeilingFcfa,
    long DebtFcfa,
    long AvailableCreditFcfa,
    bool IsActive)
{
    public static CustomerView From(Customer customer) =>
        new(
            customer.Id.Value,
            customer.Name,
            customer.Phone?.Value,
            customer.CreditCeiling.Amount,
            customer.Debt.Amount,
            customer.AvailableCredit.Amount,
            customer.IsActive);
}
