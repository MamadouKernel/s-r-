using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Application.Stock;

public interface IProductRepository
{
    Task<Product?> GetAsync(ProductId id, TenantId tenant, CancellationToken cancellationToken);

    /// <summary>Un code-barres est unique dans le commerce (CAT-01), hors le produit lui-même lors d'une modification.</summary>
    Task<bool> IsBarcodeUsedAsync(TenantId tenant, string barcode, ProductId? except, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> ListAsync(
        TenantId tenant,
        string? search,
        bool includeInactive,
        CancellationToken cancellationToken);

    /// <summary>Produits suivis au niveau du seuil d'alerte ou en dessous, les plus urgents d'abord (STK-05).</summary>
    Task<IReadOnlyList<Product>> ListLowStockAsync(TenantId tenant, CancellationToken cancellationToken);

    /// <summary>Tous les formats d'une famille, actifs ou non.</summary>
    Task<IReadOnlyList<Product>> ListByFamilyAsync(FamilyId family, TenantId tenant, CancellationToken cancellationToken);

    Task AddAsync(Product product, CancellationToken cancellationToken);

    Task UpdateAsync(Product product, CancellationToken cancellationToken);
}

public interface IProductFamilyRepository
{
    Task<ProductFamily?> GetAsync(FamilyId id, TenantId tenant, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductFamily>> ListAsync(TenantId tenant, CancellationToken cancellationToken);

    Task AddAsync(ProductFamily family, CancellationToken cancellationToken);

    Task UpdateAsync(ProductFamily family, CancellationToken cancellationToken);
}

/// <summary>Photo d'un produit ou d'une famille, privée au commerce (docs/specs/formats-produits FMT-04).</summary>
public sealed record Photo(PhotoId Id, TenantId Tenant, string ContentType, byte[] Data);

public interface IPhotoStore
{
    Task<Photo?> GetAsync(PhotoId id, TenantId tenant, CancellationToken cancellationToken);

    Task AddAsync(Photo photo, CancellationToken cancellationToken);

    /// <summary>Retire la photo (l'image est vidée) ; retirer une photo absente n'est pas une erreur.</summary>
    Task RemoveAsync(PhotoId id, TenantId tenant, CancellationToken cancellationToken);
}

public interface IStockMovementRepository
{
    Task<bool> ExistsAsync(StockMovementId id, TenantId tenant, CancellationToken cancellationToken);

    Task AddAsync(StockMovement movement, CancellationToken cancellationToken);

    /// <summary>Historique d'un produit, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<StockMovement>> ListForProductAsync(
        ProductId product,
        TenantId tenant,
        int limit,
        CancellationToken cancellationToken);
}
