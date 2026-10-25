using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Application.Stock;

public sealed record ListProductsQuery(string? Search, bool IncludeInactive);

/// <summary>Catalogue du commerce courant (CAT-03), lisible par tous les profils authentifiés.</summary>
public sealed class ListProductsHandler(ICurrentUser user, IProductRepository products, IProductFamilyRepository families)
{
    public async Task<IReadOnlyList<ProductView>> HandleAsync(
        ListProductsQuery query,
        CancellationToken cancellationToken = default)
    {
        // Un produit désactivé n'est montré qu'à qui gère le catalogue.
        var includeInactive = query.IncludeInactive && RolePolicy.CanManageCatalogue(user.Role);
        var found = await products.ListAsync(user.Tenant, query.Search, includeInactive, cancellationToken);
        var showPurchase = RolePolicy.CanSeePurchasePrice(user.Role);
        var byId = (await families.ListAsync(user.Tenant, cancellationToken)).ToDictionary(family => family.Id);
        return found.Select(product => ProductView.From(product, showPurchase, byId.GetValueOrDefault(product.FamilyId))).ToArray();
    }
}

/// <summary>Alertes de rupture (STK-05).</summary>
public sealed class ListLowStockHandler(ICurrentUser user, IProductRepository products, IProductFamilyRepository families)
{
    public async Task<Result<IReadOnlyList<ProductView>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageStock(user.Role))
        {
            return Result<IReadOnlyList<ProductView>>.Failure(
                ApplicationError.Forbidden("Ce profil ne peut pas consulter les alertes de stock."));
        }

        var found = await products.ListLowStockAsync(user.Tenant, cancellationToken);
        var byId = (await families.ListAsync(user.Tenant, cancellationToken)).ToDictionary(family => family.Id);
        return Result<IReadOnlyList<ProductView>>.Success(
            found.Select(product => ProductView.From(product, showPurchasePrice: true, byId.GetValueOrDefault(product.FamilyId))).ToArray());
    }
}

public sealed record StockMovementView(
    Guid Id,
    StockMovementKind Kind,
    int QuantityDelta,
    string? Reason,
    Guid? SourceId,
    Guid UserId,
    DateTimeOffset At,
    long? UnitCostFcfa);

public sealed record ListStockMovementsQuery(ProductId Product, int Limit);

/// <summary>Historique des mouvements d'un produit : qui, quand, combien, pourquoi.</summary>
public sealed class ListStockMovementsHandler(
    ICurrentUser user,
    IProductRepository products,
    IStockMovementRepository movements)
{
    public const int MaxLimit = 200;

    public async Task<Result<IReadOnlyList<StockMovementView>>> HandleAsync(
        ListStockMovementsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageStock(user.Role))
        {
            return Result<IReadOnlyList<StockMovementView>>.Failure(
                ApplicationError.Forbidden("Ce profil ne peut pas consulter les mouvements de stock."));
        }

        if (await products.GetAsync(query.Product, user.Tenant, cancellationToken) is null)
        {
            return Result<IReadOnlyList<StockMovementView>>.Failure(ApplicationError.NotFound("Produit introuvable."));
        }

        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        var found = await movements.ListForProductAsync(query.Product, user.Tenant, limit, cancellationToken);
        return Result<IReadOnlyList<StockMovementView>>.Success(
            found
                .Select(movement => new StockMovementView(
                    movement.Id.Value, movement.Kind, movement.QuantityDelta, movement.Reason,
                    movement.SourceId, movement.User.Value, movement.At, movement.UnitCost?.Amount))
                .ToArray());
    }
}
