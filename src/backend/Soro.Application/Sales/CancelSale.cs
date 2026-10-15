using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;

namespace Soro.Application.Sales;

public sealed record CancelSaleCommand(SaleId SaleId, string Reason);

/// <summary>
/// Annulation d'une vente validée (RG-02) : réservée aux gérants, motivée et tracée.
/// Les effets sur l'argent en caisse et le stock restent à définir (T017).
/// </summary>
public sealed class CancelSaleHandler(
    ICurrentUser user,
    ISaleRepository sales,
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
        }
        catch (DomainException exception)
        {
            return Result<SaleId>.Failure(ApplicationError.RuleViolation(exception.Message));
        }

        await audit.RecordAsync(Entry(AuditActions.SaleCancelled, command), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<SaleId>.Success(command.SaleId);
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
