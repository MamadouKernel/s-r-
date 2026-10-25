using Soro.Domain.Common;

namespace Soro.Domain.Stock;

/// <summary>
/// Famille de produits : ce que les formats partagent (nom, catégorie, photo). Chaque format reste un produit vendable complet
/// (docs/decisions/0009).
/// </summary>
public sealed class ProductFamily
{
    private const int MaxNameLength = 200;
    private const int MaxCategoryLength = 100;

    private ProductFamily(FamilyId id, TenantId tenant, string name, string? category, DateTimeOffset createdAt, PhotoId? photo)
    {
        Id = id;
        Tenant = tenant;
        Name = name;
        Category = category;
        CreatedAt = createdAt;
        PhotoId = photo;
    }

    public FamilyId Id { get; }

    public TenantId Tenant { get; }

    public string Name { get; private set; }

    public string? Category { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public PhotoId? PhotoId { get; private set; }

    public static ProductFamily Create(FamilyId id, TenantId tenant, string name, string? category, DateTimeOffset createdAt)
    {
        var family = new ProductFamily(id, tenant, string.Empty, null, createdAt, null);
        family.Rename(name, category);
        return family;
    }

    public static ProductFamily Restore(
        FamilyId id,
        TenantId tenant,
        string name,
        string? category,
        DateTimeOffset createdAt,
        PhotoId? photo) =>
        new(id, tenant, name, category, createdAt, photo);

    public void Rename(string name, string? category)
    {
        Ensure.That(!string.IsNullOrWhiteSpace(name), "Le nom du produit est obligatoire.");
        var cleanName = name.Trim();
        var cleanCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        Ensure.That(cleanName.Length <= MaxNameLength, $"Le nom ne peut pas dépasser {MaxNameLength} caractères.");
        Ensure.That((cleanCategory?.Length ?? 0) <= MaxCategoryLength, $"La catégorie ne peut pas dépasser {MaxCategoryLength} caractères.");
        Name = cleanName;
        Category = cleanCategory;
    }

    public void SetPhoto(PhotoId? photo) => PhotoId = photo;
}
