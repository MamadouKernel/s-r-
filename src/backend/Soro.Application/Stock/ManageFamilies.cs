using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Application.Stock;

/// <summary>Règles communes aux formats d'une famille (docs/specs/formats-produits FMT-01).</summary>
internal static class FamilyRules
{
    /// <summary>
    /// Le libellé d'un format au sein d'une famille de <paramref name="totalFormats"/> formats : obligatoire dès deux formats, unique parmi les
    /// autres. Renvoie le message d'erreur, ou <c>null</c> si tout va bien.
    /// </summary>
    public static string? LabelProblem(string? label, IEnumerable<string?> others, int totalFormats)
    {
        if (totalFormats > 1 && label is null)
        {
            return "Donnez un format à chaque produit de la famille (1 L, 50 kg…).";
        }

        if (label is not null && others.Any(other => other is not null && FormatLabel.Key(other) == FormatLabel.Key(label)))
        {
            return $"Le format « {label} » existe déjà dans cette famille.";
        }

        return null;
    }
}

public sealed record FormatInput(
    ProductId Id,
    string? Label,
    string? Barcode,
    long PurchasePriceFcfa,
    long SalePriceFcfa,
    int AlertThreshold,
    bool TracksStock);

public sealed record CreateFamilyCommand(FamilyId Id, string Name, string? Category, IReadOnlyList<FormatInput> Formats);

public sealed record CreateFamilyResult(FamilyId FamilyId, bool AlreadyProcessed);

/// <summary>Création d'une famille avec ses premiers formats, tout ou rien. Rejouable par identifiant de famille (RG-17).</summary>
public sealed class CreateFamilyHandler(
    ICurrentUser user,
    IProductFamilyRepository families,
    IProductRepository products,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<CreateFamilyResult>> HandleAsync(
        CreateFamilyCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageCatalogue(user.Role))
        {
            return Result<CreateFamilyResult>.Failure(ApplicationError.Forbidden("Seul un gérant peut créer un produit."));
        }

        if (await families.GetAsync(command.Id, user.Tenant, cancellationToken) is not null)
        {
            return Result<CreateFamilyResult>.Success(new(command.Id, AlreadyProcessed: true));
        }

        try
        {
            if (command.Formats.Count == 0)
            {
                return Result<CreateFamilyResult>.Failure(
                    ApplicationError.RuleViolation("Une famille a besoin d'au moins un format."));
            }

            var family = ProductFamily.Create(command.Id, user.Tenant, command.Name, command.Category, clock.UtcNow);
            var created = new List<Product>();
            foreach (var format in command.Formats)
            {
                var label = FormatLabel.Normalize(format.Label);
                var problem = FamilyRules.LabelProblem(label, created.Select(product => product.FormatLabel), command.Formats.Count);
                if (problem is not null)
                {
                    return Result<CreateFamilyResult>.Failure(ApplicationError.RuleViolation(problem));
                }

                if (await products.GetAsync(format.Id, user.Tenant, cancellationToken) is not null)
                {
                    return Result<CreateFamilyResult>.Failure(ApplicationError.RuleViolation("Ce produit existe déjà."));
                }

                var product = Product.Create(
                    format.Id,
                    user.Tenant,
                    FormatLabel.DisplayName(family.Name, label),
                    format.Barcode,
                    family.Category,
                    Money.Fcfa(format.PurchasePriceFcfa),
                    Money.Fcfa(format.SalePriceFcfa),
                    format.AlertThreshold,
                    format.TracksStock,
                    clock.UtcNow,
                    family.Id,
                    label);
                if (product.Barcode is not null
                    && (created.Any(other => other.Barcode == product.Barcode)
                        || await products.IsBarcodeUsedAsync(user.Tenant, product.Barcode, null, cancellationToken)))
                {
                    return Result<CreateFamilyResult>.Failure(CreateProductHandler.BarcodeTakenError);
                }

                created.Add(product);
            }

            await families.AddAsync(family, cancellationToken);
            foreach (var product in created)
            {
                await products.AddAsync(product, cancellationToken);
            }

            await audit.RecordAsync(
                new AuditEntry(
                    user.Tenant, user.User, AuditActions.FamilyCreated, family.Id.Value, $"{family.Name} : {created.Count} format(s)", clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<CreateFamilyResult>.Success(new(command.Id, AlreadyProcessed: false));
        }
        catch (DomainException exception)
        {
            return Result<CreateFamilyResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}

/// <param name="LabelForExistingFormat">Libellé à donner au format actuel quand la famille n'en a qu'un, sans libellé.</param>
public sealed record AddFormatCommand(FamilyId Family, FormatInput Format, string? LabelForExistingFormat);

/// <summary>Ajout d'un format à une famille existante. Rejouable par identifiant de produit.</summary>
public sealed class AddFormatHandler(
    ICurrentUser user,
    IProductFamilyRepository families,
    IProductRepository products,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<ProductId>> HandleAsync(AddFormatCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageCatalogue(user.Role))
        {
            return Result<ProductId>.Failure(ApplicationError.Forbidden("Seul un gérant peut créer un produit."));
        }

        var family = await families.GetAsync(command.Family, user.Tenant, cancellationToken);
        if (family is null)
        {
            return Result<ProductId>.Failure(ApplicationError.NotFound("Famille introuvable."));
        }

        var existing = await products.ListByFamilyAsync(family.Id, user.Tenant, cancellationToken);
        if (existing.Any(product => product.Id == command.Format.Id))
        {
            return Result<ProductId>.Success(command.Format.Id);
        }

        try
        {
            var firstLabel = FormatLabel.Normalize(command.LabelForExistingFormat);
            var unlabelled = existing.Where(product => product.FormatLabel is null).ToArray();
            if (unlabelled.Length == 1 && firstLabel is not null && existing.Count == 1)
            {
                unlabelled[0].Describe(family, firstLabel);
                await products.UpdateAsync(unlabelled[0], cancellationToken);
            }
            else if (unlabelled.Length > 0)
            {
                return Result<ProductId>.Failure(
                    ApplicationError.RuleViolation("Donnez d'abord un format au produit existant (1 L, 50 kg…)."));
            }

            var label = FormatLabel.Normalize(command.Format.Label);
            var problem = FamilyRules.LabelProblem(label, existing.Select(product => product.FormatLabel), existing.Count + 1);
            if (problem is not null)
            {
                return Result<ProductId>.Failure(ApplicationError.RuleViolation(problem));
            }

            if (await products.GetAsync(command.Format.Id, user.Tenant, cancellationToken) is not null)
            {
                return Result<ProductId>.Failure(ApplicationError.RuleViolation("Ce produit existe déjà."));
            }

            var format = command.Format;
            var product = Product.Create(
                format.Id,
                user.Tenant,
                FormatLabel.DisplayName(family.Name, label),
                format.Barcode,
                family.Category,
                Money.Fcfa(format.PurchasePriceFcfa),
                Money.Fcfa(format.SalePriceFcfa),
                format.AlertThreshold,
                format.TracksStock,
                clock.UtcNow,
                family.Id,
                label);
            if (product.Barcode is not null
                && await products.IsBarcodeUsedAsync(user.Tenant, product.Barcode, null, cancellationToken))
            {
                return Result<ProductId>.Failure(CreateProductHandler.BarcodeTakenError);
            }

            await products.AddAsync(product, cancellationToken);
            await audit.RecordAsync(
                new AuditEntry(user.Tenant, user.User, AuditActions.ProductCreated, product.Id.Value, product.Name, clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<ProductId>.Success(product.Id);
        }
        catch (DomainException exception)
        {
            return Result<ProductId>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}

public sealed record UpdateFamilyCommand(FamilyId Id, string Name, string? Category);

/// <summary>Renomme une famille : le nom affiché de chacun de ses formats suit.</summary>
public sealed class UpdateFamilyHandler(
    ICurrentUser user,
    IProductFamilyRepository families,
    IProductRepository products,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<FamilyId>> HandleAsync(UpdateFamilyCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageCatalogue(user.Role))
        {
            return Result<FamilyId>.Failure(ApplicationError.Forbidden("Seul un gérant peut modifier un produit."));
        }

        var family = await families.GetAsync(command.Id, user.Tenant, cancellationToken);
        if (family is null)
        {
            return Result<FamilyId>.Failure(ApplicationError.NotFound("Famille introuvable."));
        }

        try
        {
            var before = family.Name;
            family.Rename(command.Name, command.Category);
            foreach (var format in await products.ListByFamilyAsync(family.Id, user.Tenant, cancellationToken))
            {
                format.Describe(family, format.FormatLabel);
                await products.UpdateAsync(format, cancellationToken);
            }

            await families.UpdateAsync(family, cancellationToken);
            await audit.RecordAsync(
                new AuditEntry(
                    user.Tenant, user.User, AuditActions.ProductUpdated, family.Id.Value, $"Famille {before} → {family.Name}", clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<FamilyId>.Success(family.Id);
        }
        catch (DomainException exception)
        {
            return Result<FamilyId>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}

public sealed record GroupMember(ProductId Product, string Label);

public sealed record GroupIntoFamilyCommand(FamilyId Id, string Name, string? Category, IReadOnlyList<GroupMember> Members);

/// <summary>
/// « Regrouper dans une famille » : des produits saisis séparément deviennent les formats d'une même famille (reprise de l'existant,
/// FMT-08). Stocks, prix et ventes ne bougent pas ; seul le nom affiché change.
/// </summary>
public sealed class GroupIntoFamilyHandler(
    ICurrentUser user,
    IProductFamilyRepository families,
    IProductRepository products,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<FamilyId>> HandleAsync(GroupIntoFamilyCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageCatalogue(user.Role))
        {
            return Result<FamilyId>.Failure(ApplicationError.Forbidden("Seul un gérant peut regrouper des produits."));
        }

        if (await families.GetAsync(command.Id, user.Tenant, cancellationToken) is not null)
        {
            return Result<FamilyId>.Success(command.Id);
        }

        try
        {
            if (command.Members.Count < 2)
            {
                return Result<FamilyId>.Failure(ApplicationError.RuleViolation("Choisissez au moins deux produits à regrouper."));
            }

            var family = ProductFamily.Create(command.Id, user.Tenant, command.Name, command.Category, clock.UtcNow);
            var labels = new List<string?>();
            var members = new List<(Product Product, ProductFamily? OldFamily, string Label)>();
            foreach (var member in command.Members)
            {
                var label = FormatLabel.Normalize(member.Label);
                var problem = FamilyRules.LabelProblem(label, labels, command.Members.Count);
                if (problem is not null)
                {
                    return Result<FamilyId>.Failure(ApplicationError.RuleViolation(problem));
                }

                var product = await products.GetAsync(member.Product, user.Tenant, cancellationToken);
                if (product is null)
                {
                    return Result<FamilyId>.Failure(ApplicationError.NotFound("Produit introuvable."));
                }

                if ((await products.ListByFamilyAsync(product.FamilyId, user.Tenant, cancellationToken)).Count != 1
                    || members.Any(other => other.Product.Id == product.Id))
                {
                    return Result<FamilyId>.Failure(
                        ApplicationError.RuleViolation($"« {product.Name} » fait déjà partie d'une famille de plusieurs formats."));
                }

                labels.Add(label);
                members.Add((product, await families.GetAsync(product.FamilyId, user.Tenant, cancellationToken), label!));
            }

            await families.AddAsync(family, cancellationToken);
            foreach (var (product, oldFamily, label) in members)
            {
                if (oldFamily?.PhotoId is { } photo && product.PhotoId is null)
                {
                    product.SetPhoto(photo);
                }

                product.Describe(family, label);
                await products.UpdateAsync(product, cancellationToken);
                if (oldFamily is not null)
                {
                    await families.RemoveAsync(oldFamily, cancellationToken);
                }
            }

            await audit.RecordAsync(
                new AuditEntry(
                    user.Tenant,
                    user.User,
                    AuditActions.ProductsGrouped,
                    family.Id.Value,
                    $"{family.Name} : {string.Join(", ", members.Select(member => member.Product.Name))}",
                    clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<FamilyId>.Success(family.Id);
        }
        catch (DomainException exception)
        {
            return Result<FamilyId>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}
