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

public sealed record StockResult(StockMovementId MovementId, int QuantityOnHand, bool AlreadyProcessed);

/// <summary>Réception fournisseur (STK-01). Rejouable par identifiant de mouvement ; peut mettre à jour le prix d'achat.</summary>
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
            return Result<StockResult>.Success(new(command.MovementId, product.QuantityOnHand, AlreadyProcessed: true));
        }

        try
        {
            var movement = product.Receive(command.Quantity, command.Supplier, user.User, clock.UtcNow, command.MovementId);
            if (command.PurchasePriceFcfa is { } purchase)
            {
                product.Edit(
                    product.Name, product.Barcode, product.Category,
                    Money.Fcfa(purchase), product.SalePrice, product.AlertThreshold);
            }

            await movements.AddAsync(movement, cancellationToken);
            await products.UpdateAsync(product, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<StockResult>.Success(new(movement.Id, product.QuantityOnHand, AlreadyProcessed: false));
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
