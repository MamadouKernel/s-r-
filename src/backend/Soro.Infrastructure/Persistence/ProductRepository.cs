using Microsoft.EntityFrameworkCore;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Infrastructure.Persistence;

public sealed class ProductRepository(SoroDbContext db) : IProductRepository
{
    public async Task<Product?> GetAsync(ProductId id, TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.Products.SingleOrDefaultAsync(
            product => product.Id == id.Value && product.TenantId == tenant.Value,
            cancellationToken);
        return row is null ? null : ProductMapper.ToDomain(row);
    }

    public async Task<IReadOnlyList<string>> RecentInternalBarcodesAsync(TenantId tenant, int count, CancellationToken cancellationToken) =>
        await db.Products
            .AsNoTracking()
            .Where(product => product.TenantId == tenant.Value
                && product.Barcode != null
                && product.Barcode.StartsWith(InternalBarcode.Prefix)
                && product.Barcode.Length == 13)
            .Select(product => product.Barcode!)
            .OrderByDescending(code => code)
            .Take(count)
            .ToListAsync(cancellationToken);

    public Task<bool> IsBarcodeUsedAsync(
        TenantId tenant,
        string barcode,
        ProductId? except,
        CancellationToken cancellationToken) =>
        db.Products.AnyAsync(
            product => product.TenantId == tenant.Value
                && product.Barcode == barcode
                && (except == null || product.Id != except.Value.Value),
            cancellationToken);

    public async Task<IReadOnlyList<Product>> ListAsync(
        TenantId tenant,
        string? search,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking().Where(product => product.TenantId == tenant.Value);
        if (!includeInactive)
        {
            query = query.Where(product => product.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(product =>
                product.Barcode == term || EF.Functions.ILike(product.Name, "%" + EscapeLike(term) + "%"));
        }

        var rows = await query.OrderBy(product => product.Name).ToListAsync(cancellationToken);
        return rows.Select(ProductMapper.ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<Product>> ListLowStockAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        var rows = await db.Products.AsNoTracking()
            .Where(product => product.TenantId == tenant.Value
                && product.IsActive
                && product.TracksStock
                && product.AlertThreshold > 0
                && product.QuantityOnHand <= product.AlertThreshold)
            .OrderBy(product => product.QuantityOnHand)
            .ThenBy(product => product.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(ProductMapper.ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<Product>> ListByFamilyAsync(FamilyId family, TenantId tenant, CancellationToken cancellationToken)
    {
        var rows = await db.Products
            .Where(product => product.TenantId == tenant.Value && product.FamilyId == family.Value)
            .ToListAsync(cancellationToken);
        return rows.Select(ProductMapper.ToDomain).ToArray();
    }

    public Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        db.Products.Add(ProductMapper.ToRow(product));
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken)
    {
        var row = await db.Products.FindAsync([product.Id.Value], cancellationToken)
            ?? throw new InvalidOperationException($"Le produit {product.Id.Value} n'existe pas.");
        ProductMapper.Apply(row, product);
    }

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
