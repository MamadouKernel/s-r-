using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Application.Stock;

public sealed record CreateProductCommand(
    ProductId Id,
    string Name,
    string? Barcode,
    string? Category,
    long PurchasePriceFcfa,
    long SalePriceFcfa,
    int AlertThreshold,
    bool TracksStock);

public sealed record CreateProductResult(ProductId ProductId, bool AlreadyProcessed);

/// <summary>Création d'une fiche produit (CAT-01). Rejouable par identifiant de produit (RG-17).</summary>
public sealed class CreateProductHandler(
    ICurrentUser user,
    IProductRepository products,
    IProductFamilyRepository families,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<CreateProductResult>> HandleAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageCatalogue(user.Role))
        {
            return Result<CreateProductResult>.Failure(
                ApplicationError.Forbidden("Seul un gérant peut créer un produit."));
        }

        if (await products.GetAsync(command.Id, user.Tenant, cancellationToken) is not null)
        {
            return Result<CreateProductResult>.Success(new(command.Id, AlreadyProcessed: true));
        }

        try
        {
            var product = Product.Create(
                command.Id,
                user.Tenant,
                command.Name,
                command.Barcode,
                command.Category,
                Money.Fcfa(command.PurchasePriceFcfa),
                Money.Fcfa(command.SalePriceFcfa),
                command.AlertThreshold,
                command.TracksStock,
                clock.UtcNow);

            if (await BarcodeConflictAsync(product.Barcode, null, cancellationToken))
            {
                return Result<CreateProductResult>.Failure(BarcodeTakenError);
            }

            // Un produit créé seul est sa propre famille, à un format sans libellé (même identifiant).
            await families.AddAsync(
                ProductFamily.Create(product.FamilyId, user.Tenant, product.Name, product.Category, clock.UtcNow), cancellationToken);
            await products.AddAsync(product, cancellationToken);
            await audit.RecordAsync(
                new AuditEntry(user.Tenant, user.User, AuditActions.ProductCreated, product.Id.Value, product.Name, clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<CreateProductResult>.Success(new(command.Id, AlreadyProcessed: false));
        }
        catch (DomainException exception)
        {
            return Result<CreateProductResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }

    private async Task<bool> BarcodeConflictAsync(string? barcode, ProductId? except, CancellationToken cancellationToken) =>
        barcode is not null && await products.IsBarcodeUsedAsync(user.Tenant, barcode, except, cancellationToken);

    internal static ApplicationError BarcodeTakenError { get; } =
        new(ErrorCodes.BarcodeTaken, "Ce code-barres est déjà utilisé par un autre produit.");
}

public sealed record UpdateProductCommand(
    ProductId Id,
    string Name,
    string? Barcode,
    string? Category,
    long PurchasePriceFcfa,
    long SalePriceFcfa,
    int AlertThreshold,
    string? FormatLabel = null);

/// <summary>
/// Modification d'une fiche produit (CAT-02). Les changements de prix sont tracés. Le nom et la catégorie sont ceux de la <b>famille</b> (tous
/// ses formats suivent) ; le libellé est celui du format.
/// </summary>
public sealed class UpdateProductHandler(
    ICurrentUser user,
    IProductRepository products,
    IProductFamilyRepository families,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<ProductId>> HandleAsync(
        UpdateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageCatalogue(user.Role))
        {
            return Result<ProductId>.Failure(ApplicationError.Forbidden("Seul un gérant peut modifier un produit."));
        }

        var product = await products.GetAsync(command.Id, user.Tenant, cancellationToken);
        if (product is null)
        {
            return Result<ProductId>.Failure(ApplicationError.NotFound("Produit introuvable."));
        }

        try
        {
            var before = product.SalePrice;
            var family = await families.GetAsync(product.FamilyId, user.Tenant, cancellationToken);
            var isNewFamily = family is null;
            family ??= ProductFamily.Create(product.FamilyId, user.Tenant, product.Name, product.Category, clock.UtcNow);
            var siblings = (await products.ListByFamilyAsync(product.FamilyId, user.Tenant, cancellationToken))
                .Where(other => other.Id != product.Id)
                .ToArray();
            var label = FormatLabel.Normalize(command.FormatLabel);
            var labelProblem = FamilyRules.LabelProblem(label, siblings.Select(other => other.FormatLabel), siblings.Length + 1);
            if (labelProblem is not null)
            {
                return Result<ProductId>.Failure(ApplicationError.RuleViolation(labelProblem));
            }

            var familyChanged = family.Name != command.Name.Trim() || family.Category != Clean(command.Category);
            family.Rename(command.Name, command.Category);
            product.Edit(
                command.Name,
                command.Barcode,
                command.Category,
                Money.Fcfa(command.PurchasePriceFcfa),
                Money.Fcfa(command.SalePriceFcfa),
                command.AlertThreshold);
            product.Describe(family, label);

            if (product.Barcode is not null
                && await products.IsBarcodeUsedAsync(user.Tenant, product.Barcode, product.Id, cancellationToken))
            {
                return Result<ProductId>.Failure(CreateProductHandler.BarcodeTakenError);
            }

            if (isNewFamily)
            {
                await families.AddAsync(family, cancellationToken);
            }
            else if (familyChanged)
            {
                await families.UpdateAsync(family, cancellationToken);
            }

            foreach (var sibling in siblings.Where(_ => familyChanged))
            {
                sibling.Describe(family, sibling.FormatLabel);
                await products.UpdateAsync(sibling, cancellationToken);
            }

            await products.UpdateAsync(product, cancellationToken);
            await audit.RecordAsync(
                new AuditEntry(
                    user.Tenant,
                    user.User,
                    AuditActions.ProductUpdated,
                    product.Id.Value,
                    $"{product.Name} : prix de vente {before} → {product.SalePrice}",
                    clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<ProductId>.Success(product.Id);
        }
        catch (DomainException exception)
        {
            return Result<ProductId>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record DeactivateProductCommand(ProductId Id);

/// <summary>Un produit ne se supprime jamais : il se désactive (CAT-02).</summary>
public sealed class DeactivateProductHandler(
    ICurrentUser user,
    IProductRepository products,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<ProductId>> HandleAsync(
        DeactivateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageCatalogue(user.Role))
        {
            return Result<ProductId>.Failure(ApplicationError.Forbidden("Seul un gérant peut désactiver un produit."));
        }

        var product = await products.GetAsync(command.Id, user.Tenant, cancellationToken);
        if (product is null)
        {
            return Result<ProductId>.Failure(ApplicationError.NotFound("Produit introuvable."));
        }

        if (product.IsActive)
        {
            product.Deactivate();
            await products.UpdateAsync(product, cancellationToken);
            await audit.RecordAsync(
                new AuditEntry(user.Tenant, user.User, AuditActions.ProductDeactivated, product.Id.Value, product.Name, clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Result<ProductId>.Success(product.Id);
    }
}
