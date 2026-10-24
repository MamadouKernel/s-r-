using Soro.Application.Common;
using Soro.Application.Customers;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Sales;

public sealed record CancelSaleCommand(SaleId SaleId, string Reason);

/// <summary>
/// Annulation d'une vente validée (RG-02) : réservée aux gérants, motivée et tracée.
/// Les effets sur l'argent en caisse et le stock restent à définir (T017).
/// </summary>
public sealed class CancelSaleHandler(
    ICurrentUser user,
    ISaleRepository sales,
    ICustomerRepository customers,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<SaleId>> HandleAsync(
        CancelSaleCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.IsManager(user.Role))
        {
            return await RefuseAsync(command, cancellationToken);
        }

        var sale = await sales.GetAsync(command.SaleId, user.Tenant, cancellationToken);
        if (sale is null)
        {
            return Result<SaleId>.Failure(ApplicationError.NotFound("Vente introuvable."));
        }

        try
        {
            var cancelled = sale.Cancel(user.User, command.Reason, clock.UtcNow);
            await sales.UpdateAsync(cancelled, cancellationToken);
            await ReleaseCreditAsync(sale, cancellationToken);
        }
        catch (DomainException exception)
        {
            return Result<SaleId>.Failure(ApplicationError.RuleViolation(exception.Message));
        }

        await audit.RecordAsync(Entry(AuditActions.SaleCancelled, command), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<SaleId>.Success(command.SaleId);
    }

    /// <summary>Annuler une vente à crédit annule la dette correspondante (CRD-06) ; le reste de l'annulation dépend de T017.</summary>
    private async Task ReleaseCreditAsync(Domain.Sales.Sale sale, CancellationToken cancellationToken)
    {
        var credit = Money.Sum(sale.Payments.Where(payment => payment.Method == Domain.Sales.PaymentMethod.Credit).Select(payment => payment.Amount));
        if (credit == Money.Zero || sale.CustomerId is not { } customerId)
        {
            return;
        }

        var customer = await customers.GetAsync(customerId, user.Tenant, cancellationToken);
        if (customer is not null)
        {
            customer.Discharge(credit);
            await customers.UpdateAsync(customer, cancellationToken);
        }
    }

    private async Task<Result<SaleId>> RefuseAsync(CancelSaleCommand command, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(Entry(AuditActions.CancelRefused, command), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<SaleId>.Failure(ApplicationError.Forbidden("Seul un gérant peut annuler une vente validée."));
    }

    private AuditEntry Entry(string action, CancelSaleCommand command) =>
        new(user.Tenant, user.User, action, command.SaleId.Value, command.Reason, clock.UtcNow);
}
