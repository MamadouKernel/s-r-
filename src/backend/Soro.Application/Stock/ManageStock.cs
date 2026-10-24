using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Application.Stock;

public sealed record ReceiveStockCommand(
    StockMovementId MovementId,
    ProductId Product,
    int Quantity,
    string? Supplier,
    long? PurchasePriceFcfa);

/// <param name="MovementId">Le mouvement créé (ou déjà traité).</param>
/// <param name="QuantityOnHand">Le stock après l'opération.</param>
/// <param name="AlreadyProcessed">Vrai si l'identifiant avait déjà été traité : rien n'est recalculé.</param>
/// <param name="PurchasePriceFcfa">Le prix d'achat moyen après une réception (RG-11) ; <c>null</c> pour une perte.</param>
public sealed record StockResult(StockMovementId MovementId, int QuantityOnHand, bool AlreadyProcessed, long? PurchasePriceFcfa = null);

/// <summary>
/// Réception fournisseur (STK-01). Rejouable par identifiant de mouvement. Le prix indiqué est celui de la <b>livraison</b> : le prix d'achat
/// du produit devient la moyenne pondérée (RG-11, docs/decisions/0007).
/// </summary>
public sealed class ReceiveStockHandler(
    ICurrentUser user,
    IProductRepository products,
    IStockMovementRepository movements,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<StockResult>> HandleAsync(
        ReceiveStockCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageStock(user.Role))
        {
            return Result<StockResult>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas réceptionner du stock."));
        }

        var product = await products.GetAsync(command.Product, user.Tenant, cancellationToken);
        if (product is null)
        {
            return Result<StockResult>.Failure(ApplicationError.NotFound("Produit introuvable."));
        }

        if (await movements.ExistsAsync(command.MovementId, user.Tenant, cancellationToken))
        {
            return Result<StockResult>.Success(
                new(command.MovementId, product.QuantityOnHand, AlreadyProcessed: true, product.PurchasePrice.Amount));
        }

        try
        {
            var deliveryCost = command.PurchasePriceFcfa is { } purchase ? Money.Fcfa(purchase) : (Money?)null;
            var movement = product.Receive(command.Quantity, command.Supplier, user.User, clock.UtcNow, command.MovementId, deliveryCost);

            await movements.AddAsync(movement, cancellationToken);
            await products.UpdateAsync(product, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<StockResult>.Success(
                new(movement.Id, product.QuantityOnHand, AlreadyProcessed: false, product.PurchasePrice.Amount));
        }
        catch (DomainException exception)
        {
            return Result<StockResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}

public sealed record RecordStockLossCommand(StockMovementId MovementId, ProductId Product, int Quantity, string Reason);

/// <summary>Perte, casse ou consommation interne (STK-02) : motif obligatoire, action tracée.</summary>
public sealed class RecordStockLossHandler(
    ICurrentUser user,
    IProductRepository products,
    IStockMovementRepository movements,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<StockResult>> HandleAsync(
        RecordStockLossCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageStock(user.Role))
        {
            return Result<StockResult>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas déclarer une perte."));
        }

        var product = await products.GetAsync(command.Product, user.Tenant, cancellationToken);
        if (product is null)
        {
            return Result<StockResult>.Failure(ApplicationError.NotFound("Produit introuvable."));
        }

        if (await movements.ExistsAsync(command.MovementId, user.Tenant, cancellationToken))
        {
            return Result<StockResult>.Success(new(command.MovementId, product.QuantityOnHand, AlreadyProcessed: true));
        }

        try
        {
            var movement = product.RecordLoss(command.Quantity, command.Reason, user.User, clock.UtcNow, command.MovementId);
            await movements.AddAsync(movement, cancellationToken);
            await products.UpdateAsync(product, cancellationToken);
            await audit.RecordAsync(
                new AuditEntry(
                    user.Tenant,
                    user.User,
                    AuditActions.StockLoss,
                    product.Id.Value,
                    $"{product.Name} : -{command.Quantity} ({command.Reason.Trim()})",
                    clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<StockResult>.Success(new(movement.Id, product.QuantityOnHand, AlreadyProcessed: false));
        }
        catch (DomainException exception)
        {
            return Result<StockResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}
