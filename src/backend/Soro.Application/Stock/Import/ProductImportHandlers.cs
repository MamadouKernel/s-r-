using System.Security.Cryptography;
using System.Text;
using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Application.Stock;

/// <summary>Compte rendu de la vérification ou de l'importation (docs/specs/import-excel IMP-05, IMP-06).</summary>
public sealed record ImportReport(
    int RowsRead,
    int NewProducts,
    int NewFormats,
    int StockMovements,
    int SkippedCount,
    IReadOnlyList<ImportSkip> Skipped,
    int ProblemCount,
    IReadOnlyList<ImportProblem> Problems,
    bool CanImport,
    bool Imported,
    bool AlreadyProcessed);

public sealed record ProductImportQuery(byte[] File);

public sealed record ImportProductsCommand(Guid ImportId, byte[] File);

/// <summary>Limites publiques de l'import, partagées avec l'API.</summary>
public static class ProductImportLimits
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxRows = 2000;
}

/// <summary>Ce que tous les cas d'usage de l'import vérifient avant de lire le fichier, et la fabrication du compte rendu.</summary>
internal static class ProductImportSupport
{
    public const int MaxBytes = ProductImportLimits.MaxBytes;
    public const int MaxRows = ProductImportLimits.MaxRows;
    private static readonly byte[] ZipHeader = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] OldExcelHeader = [0xD0, 0xCF, 0x11, 0xE0];

    /// <summary>Taille et nature du fichier, jugées sur le contenu : le nom ne prouve rien.</summary>
    public static ApplicationError? CheckFile(byte[] file)
    {
        if (file.Length > MaxBytes)
        {
            return ApplicationError.RuleViolation("Ce fichier est trop lourd (2 Mo au plus). Coupez-le en plusieurs fichiers.");
        }

        if (file.Length >= 4 && file.AsSpan(0, 4).SequenceEqual(OldExcelHeader))
        {
            return ApplicationError.RuleViolation(
                "Ce fichier est un ancien classeur Excel (.xls) ou protégé par un mot de passe. Enregistrez-le au format .xlsx, sans mot de passe.");
        }

        if (file.Length < 4 || !file.AsSpan(0, 4).SequenceEqual(ZipHeader))
        {
            return ApplicationError.RuleViolation("Ce fichier n'est pas un classeur Excel (.xlsx). Utilisez le modèle à télécharger.");
        }

        return null;
    }

    public static async Task<CatalogueSnapshot> SnapshotAsync(
        TenantId tenant, IProductFamilyRepository families, IProductRepository products, CancellationToken cancellationToken)
    {
        var allProducts = await products.ListAsync(tenant, null, true, cancellationToken);
        var allFamilies = await families.ListAsync(tenant, cancellationToken);
        var byFamily = allProducts.ToLookup(product => product.FamilyId);
        return new CatalogueSnapshot(
            allFamilies
                .Select(family => new ExistingFamily(
                    family.Id, family.Name, family.Category, byFamily[family.Id].Select(product => new ExistingFormat(product.FormatLabel, product.Barcode)).ToArray()))
                .ToArray(),
            allProducts.Where(product => product.Barcode is not null).Select(product => product.Barcode!).ToHashSet());
    }

    public static ImportReport Report(ImportPlan plan, bool imported, bool alreadyProcessed) =>
        new(
            plan.RowsRead,
            plan.NewFamilies.Count,
            plan.NewFormats,
            plan.NewFamilies.SelectMany(family => family.Formats).Concat(plan.Additions.Select(addition => addition.Format)).Count(format => format.InitialStock > 0),
            plan.Skipped.Count,
            plan.Skipped.Take(ProductImportPlanner.MaxListedProblems).ToArray(),
            plan.ProblemCount,
            plan.Problems,
            plan.CanImport,
            imported,
            alreadyProcessed);

    /// <summary>Identifiant stable déduit de l'import et de la clé du produit : rejouer l'import retombe sur les mêmes identifiants.</summary>
    public static Guid DeterministicId(Guid importId, string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{importId:N}|{key}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static string FamilyKey(string name) => "f|" + TextKey.Key(name);

    public static string ProductKey(string familyName, string? label) => "p|" + TextKey.Key(familyName) + "|" + (label is null ? string.Empty : TextKey.Key(label));
}

/// <summary>Le classeur modèle à remplir (IMP-01).</summary>
public sealed class GetImportTemplateHandler(ICurrentUser user, IProductTemplateWriter writer)
{
    public Task<Result<byte[]>> HandleAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(
            RolePolicy.CanManageCatalogue(user.Role)
                ? Result<byte[]>.Success(writer.Write())
                : Result<byte[]>.Failure(ApplicationError.Forbidden("Seul un gérant peut importer des produits.")));
}

/// <summary>Vérifie un fichier sans rien enregistrer (IMP-05).</summary>
public sealed class PreviewProductImportHandler(
    ICurrentUser user,
    IProductSheetReader reader,
    IProductFamilyRepository families,
    IProductRepository products)
{
    public async Task<Result<ImportReport>> HandleAsync(ProductImportQuery query, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageCatalogue(user.Role))
        {
            return Result<ImportReport>.Failure(ApplicationError.Forbidden("Seul un gérant peut importer des produits."));
        }

        if (ProductImportSupport.CheckFile(query.File) is { } refused)
        {
            return Result<ImportReport>.Failure(refused);
        }

        var read = reader.Read(query.File);
        if (read.FileProblem is not null)
        {
            return Result<ImportReport>.Failure(ApplicationError.RuleViolation(read.FileProblem));
        }

        if (read.Rows.Count > ProductImportSupport.MaxRows)
        {
            return Result<ImportReport>.Failure(
                ApplicationError.RuleViolation("Ce fichier a plus de 2 000 lignes. Coupez-le en plusieurs fichiers."));
        }

        var catalogue = await ProductImportSupport.SnapshotAsync(user.Tenant, families, products, cancellationToken);
        var plan = ProductImportPlanner.Plan(read.Rows, catalogue);
        return Result<ImportReport>.Success(ProductImportSupport.Report(plan, imported: false, alreadyProcessed: false));
    }
}

/// <summary>
/// Importe un fichier, tout ou rien (IMP-06) : le plan est refait ici, jamais repris d'une vérification précédente. Rejouable : les
/// identifiants sont déduits de l'import, du nom et du format.
/// </summary>
public sealed class ImportProductsHandler(
    ICurrentUser user,
    IProductSheetReader reader,
    IProductFamilyRepository families,
    IProductRepository products,
    IStockMovementRepository movements,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public const string Supplier = "Import Excel";

    public async Task<Result<ImportReport>> HandleAsync(ImportProductsCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageCatalogue(user.Role))
        {
            return Result<ImportReport>.Failure(ApplicationError.Forbidden("Seul un gérant peut importer des produits."));
        }

        if (ProductImportSupport.CheckFile(command.File) is { } refused)
        {
            return Result<ImportReport>.Failure(refused);
        }

        var read = reader.Read(command.File);
        if (read.FileProblem is not null)
        {
            return Result<ImportReport>.Failure(ApplicationError.RuleViolation(read.FileProblem));
        }

        if (read.Rows.Count > ProductImportSupport.MaxRows)
        {
            return Result<ImportReport>.Failure(
                ApplicationError.RuleViolation("Ce fichier a plus de 2 000 lignes. Coupez-le en plusieurs fichiers."));
        }

        var catalogue = await ProductImportSupport.SnapshotAsync(user.Tenant, families, products, cancellationToken);
        var plan = ProductImportPlanner.Plan(read.Rows, catalogue);
        if (!plan.CanImport)
        {
            var already = plan.ProblemCount == 0 && catalogue.Families.Any(family => family.Id.Value == ProductImportSupport.DeterministicId(command.ImportId, ProductImportSupport.FamilyKey(family.Name)));
            return Result<ImportReport>.Success(ProductImportSupport.Report(plan, imported: false, alreadyProcessed: already));
        }

        try
        {
            foreach (var planned in plan.NewFamilies)
            {
                var family = ProductFamily.Create(
                    new FamilyId(ProductImportSupport.DeterministicId(command.ImportId, ProductImportSupport.FamilyKey(planned.Name))),
                    user.Tenant,
                    planned.Name,
                    planned.Category,
                    clock.UtcNow);
                await families.AddAsync(family, cancellationToken);
                foreach (var format in planned.Formats)
                {
                    await CreateFormatAsync(family, format, command.ImportId, cancellationToken);
                }
            }

            foreach (var addition in plan.Additions)
            {
                var family = await families.GetAsync(addition.Family, user.Tenant, cancellationToken)
                    ?? throw new InvalidOperationException("La famille a disparu pendant l'import.");
                await CreateFormatAsync(family, addition.Format, command.ImportId, cancellationToken);
            }

            await audit.RecordAsync(
                new AuditEntry(
                    user.Tenant,
                    user.User,
                    AuditActions.ProductsImported,
                    command.ImportId,
                    $"{plan.NewFamilies.Count} produit(s), {plan.NewFormats} format(s)",
                    clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<ImportReport>.Success(ProductImportSupport.Report(plan, imported: true, alreadyProcessed: false));
        }
        catch (DomainException exception)
        {
            return Result<ImportReport>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }

    /// <summary>Crée un format et, s'il y a un stock de départ, la réception qui le porte.</summary>
    private async Task CreateFormatAsync(ProductFamily family, PlannedFormat format, Guid importId, CancellationToken cancellationToken)
    {
        var key = ProductImportSupport.ProductKey(family.Name, format.Label);
        var product = Product.Create(
            new ProductId(ProductImportSupport.DeterministicId(importId, key)),
            user.Tenant,
            FormatLabel.DisplayName(family.Name, format.Label),
            format.Barcode,
            family.Category,
            Money.Fcfa(format.PurchasePriceFcfa),
            Money.Fcfa(format.SalePriceFcfa),
            format.AlertThreshold,
            format.TracksStock,
            clock.UtcNow,
            family.Id,
            format.Label,
            format.Unit);
        if (format.InitialStock <= 0)
        {
            await products.AddAsync(product, cancellationToken);
            return;
        }

        // La réception se fait avant l'enregistrement : le dépôt copie l'état du produit au moment où il l'ajoute.
        var movement = product.Receive(
            format.InitialStock,
            Supplier,
            user.User,
            clock.UtcNow,
            new StockMovementId(ProductImportSupport.DeterministicId(importId, "m|" + key)),
            format.PurchasePriceFcfa > 0 ? Money.Fcfa(format.PurchasePriceFcfa) : null);
        await products.AddAsync(product, cancellationToken);
        await movements.AddAsync(movement, cancellationToken);
    }
}
