using System.Globalization;
using System.Text.RegularExpressions;
using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Application.Stock;

public sealed record ImportProblem(int Line, string Column, string Message)
{
    /// <summary>« Ligne 12 : le prix de vente doit être… » : ce que l'écran affiche.</summary>
    public string Text => $"Ligne {Line} : {Message}";
}

public sealed record ImportSkip(int Line, string Name, string Reason);

public sealed record PlannedFormat(
    int Line, string? Label, string? Barcode, long PurchasePriceFcfa, long SalePriceFcfa, int AlertThreshold, bool TracksStock, int InitialStock,
    MeasureUnit Unit = MeasureUnit.Piece);

public sealed record PlannedFamily(string Name, string? Category, IReadOnlyList<PlannedFormat> Formats);

/// <summary>Un format qui rejoint une famille déjà dans le catalogue.</summary>
public sealed record PlannedAddition(FamilyId Family, PlannedFormat Format);

public sealed record ExistingFormat(string? Label, string? Barcode);

public sealed record ExistingFamily(FamilyId Id, string Name, string? Category, IReadOnlyList<ExistingFormat> Formats);

/// <summary>Le catalogue du commerce tel que l'import le compare au fichier.</summary>
public sealed record CatalogueSnapshot(IReadOnlyList<ExistingFamily> Families, ISet<string> Barcodes);

public sealed record ImportPlan(
    int RowsRead,
    int ProblemCount,
    IReadOnlyList<ImportProblem> Problems,
    IReadOnlyList<ImportSkip> Skipped,
    IReadOnlyList<PlannedFamily> NewFamilies,
    IReadOnlyList<PlannedAddition> Additions)
{
    public int NewFormats => NewFamilies.Sum(family => family.Formats.Count) + Additions.Count;

    public bool CanImport => ProblemCount == 0 && NewFormats > 0;
}

/// <summary>
/// Vérifie les lignes d'un fichier d'import et prépare ce qui serait créé, sans rien écrire (docs/specs/import-excel IMP-03 à IMP-06).
/// Une seule règle pour la vérification et pour l'importation : le plan est refait à chaque fois.
/// </summary>
public static partial class ProductImportPlanner
{
    public const int MaxListedProblems = 50;
    private const int MaxName = 200;
    private const int MaxCategory = 100;
    private const int MaxBarcode = 64;

    private sealed record Parsed(
        ImportRow Row, string Name, string? Label, string? Category, string? Barcode, long Purchase, long Sale, int Alert, int Stock, bool Tracks, MeasureUnit Unit);

    public static ImportPlan Plan(IReadOnlyList<ImportRow> rows, CatalogueSnapshot catalogue)
    {
        var problems = new List<ImportProblem>();
        var skipped = new List<ImportSkip>();
        var parsed = new List<Parsed>();
        var rowsRead = 0;

        foreach (var row in rows)
        {
            if (ImportColumns.All.All(column => string.IsNullOrWhiteSpace(row.Get(column))))
            {
                continue;
            }

            rowsRead++;
            var before = problems.Count;
            var item = ParseRow(row, problems);
            if (problems.Count == before && item is not null)
            {
                parsed.Add(item);
            }
        }

        var fileBarcodes = new Dictionary<string, int>();
        var newFamilies = new List<PlannedFamily>();
        var additions = new List<PlannedAddition>();

        foreach (var group in parsed.GroupBy(item => TextKey.Key(item.Name)))
        {
            var existing = catalogue.Families.FirstOrDefault(family => TextKey.Key(family.Name) == group.Key);
            var labelLines = new Dictionary<string, (int Line, string? Label)>();
            var existingLabels = existing?.Formats.Select(format => LabelKey(format.Label)).ToHashSet() ?? [];
            var fresh = new List<Parsed>();

            foreach (var item in group)
            {
                if (item.Barcode is not null)
                {
                    if (fileBarcodes.TryGetValue(item.Barcode, out var firstLine))
                    {
                        problems.Add(new(item.Row.Line, ImportColumns.Title(ImportColumn.Barcode), $"ce code-barres est déjà utilisé ligne {firstLine}."));
                        continue;
                    }

                    fileBarcodes[item.Barcode] = item.Row.Line;
                }

                var key = LabelKey(item.Label);
                if (labelLines.TryGetValue(key, out var same))
                {
                    problems.Add(new(
                        item.Row.Line,
                        ImportColumns.Title(ImportColumn.Format),
                        $"le format « {same.Label} » apparaît déjà pour ce produit (ligne {same.Line})."));
                    continue;
                }

                labelLines[key] = (item.Row.Line, item.Label);

                if (item.Barcode is not null && catalogue.Barcodes.Contains(item.Barcode))
                {
                    skipped.Add(new(item.Row.Line, item.Name, "ce code-barres est déjà dans votre catalogue."));
                }
                else if (existing is not null && existingLabels.Contains(key))
                {
                    skipped.Add(new(item.Row.Line, item.Name, "ce produit est déjà dans votre catalogue."));
                }
                else
                {
                    fresh.Add(item);
                }
            }

            if (fresh.Count == 0)
            {
                continue;
            }

            if (existing is not null)
            {
                PlanAdditions(existing, fresh, problems, additions);
            }
            else
            {
                PlanNewFamily(group.ToList(), fresh, problems, newFamilies);
            }
        }

        var ordered = problems.OrderBy(problem => problem.Line).ThenBy(problem => problem.Column, StringComparer.Ordinal).ToList();
        var blocked = ordered.Count > 0;
        return new ImportPlan(
            rowsRead,
            ordered.Count,
            ordered.Take(MaxListedProblems).ToArray(),
            skipped.OrderBy(skip => skip.Line).ToArray(),
            blocked ? [] : newFamilies,
            blocked ? [] : additions);
    }

    private static void PlanAdditions(ExistingFamily existing, List<Parsed> fresh, List<ImportProblem> problems, List<PlannedAddition> additions)
    {
        if (existing.Formats.Any(format => format.Label is null))
        {
            problems.Add(new(
                fresh[0].Row.Line,
                ImportColumns.Title(ImportColumn.Format),
                $"« {existing.Name} » existe déjà sans format : ouvrez sa fiche dans Gestion pour lui donner un format avant d'en ajouter."));
            return;
        }

        foreach (var item in fresh)
        {
            if (item.Label is null)
            {
                problems.Add(new(item.Row.Line, ImportColumns.Title(ImportColumn.Format), "donnez un format à ce produit (1 L, 50 kg…) : « " + existing.Name + " » a déjà plusieurs formats."));
                continue;
            }

            additions.Add(new(existing.Id, ToFormat(item)));
        }
    }

    private static void PlanNewFamily(List<Parsed> all, List<Parsed> fresh, List<ImportProblem> problems, List<PlannedFamily> newFamilies)
    {
        string? category = null;
        Parsed? categoryRow = null;
        foreach (var item in all.Where(item => item.Category is not null))
        {
            if (categoryRow is null)
            {
                category = item.Category;
                categoryRow = item;
            }
            else if (TextKey.Key(item.Category!) != TextKey.Key(category!))
            {
                problems.Add(new(
                    item.Row.Line,
                    ImportColumns.Title(ImportColumn.Category),
                    $"cette ligne donne la catégorie « {item.Category} » mais la ligne {categoryRow.Row.Line} donne « {category} » pour le même produit."));
            }
        }

        if (fresh.Count > 1)
        {
            foreach (var item in fresh.Where(item => item.Label is null))
            {
                problems.Add(new(item.Row.Line, ImportColumns.Title(ImportColumn.Format), "donnez un format à chaque ligne de ce produit (1 L, 50 kg…)."));
            }
        }

        newFamilies.Add(new PlannedFamily(fresh[0].Name, category, fresh.Select(ToFormat).ToArray()));
    }

    private static PlannedFormat ToFormat(Parsed item) =>
        new(item.Row.Line, item.Label, item.Barcode, item.Purchase, item.Sale, item.Alert, item.Tracks, item.Stock, item.Unit);

    private static string LabelKey(string? label) => label is null ? string.Empty : TextKey.Key(label);

    private static Parsed? ParseRow(ImportRow row, List<ImportProblem> problems)
    {
        var ok = true;
        void Fail(ImportColumn column, string message)
        {
            problems.Add(new(row.Line, ImportColumns.Title(column), message));
            ok = false;
        }

        var name = Clean(row.Get(ImportColumn.Name));
        if (name is null)
        {
            Fail(ImportColumn.Name, "le nom du produit est obligatoire.");
        }
        else if (name.Length > MaxName)
        {
            Fail(ImportColumn.Name, $"le nom du produit ne peut pas dépasser {MaxName} caractères.");
        }

        string? label = null;
        try
        {
            label = FormatLabel.Normalize(row.Get(ImportColumn.Format));
        }
        catch (DomainException)
        {
            Fail(ImportColumn.Format, $"le format ne peut pas dépasser {FormatLabel.MaxLength} caractères.");
        }

        var category = Clean(row.Get(ImportColumn.Category));
        if (category is { Length: > MaxCategory })
        {
            Fail(ImportColumn.Category, $"la catégorie ne peut pas dépasser {MaxCategory} caractères.");
        }

        var barcode = Clean(row.Get(ImportColumn.Barcode));
        if (barcode is not null && ScientificNotation().IsMatch(barcode))
        {
            Fail(ImportColumn.Barcode, "Excel a transformé ce code-barres en nombre (écriture avec « E+ »). Dans Excel, mettez la colonne en format Texte, puis retapez le code.");
        }
        else if (barcode is { Length: > MaxBarcode })
        {
            Fail(ImportColumn.Barcode, $"le code-barres ne peut pas dépasser {MaxBarcode} caractères.");
        }

        long purchase = 0;
        long sale = 0;
        long alert = 0;
        long stock = 0;
        var purchaseText = Clean(row.Get(ImportColumn.PurchasePrice));
        if (purchaseText is not null && !TryAmount(purchaseText, out purchase))
        {
            Fail(ImportColumn.PurchasePrice, "le prix d'achat doit être un nombre entier en FCFA, sans centimes.");
        }

        var saleText = Clean(row.Get(ImportColumn.SalePrice));
        if (saleText is null)
        {
            Fail(ImportColumn.SalePrice, "le prix de vente est obligatoire.");
        }
        else if (!TryAmount(saleText, out sale))
        {
            Fail(ImportColumn.SalePrice, "le prix de vente doit être un nombre entier en FCFA, sans centimes.");
        }

        var unit = MeasureUnit.Piece;
        var unitText = Clean(row.Get(ImportColumn.Unit));
        if (unitText is not null && !TryUnit(unitText, out unit))
        {
            Fail(ImportColumn.Unit, "l'unité doit être « pièce », « kg » ou « litre » (laissez vide pour une pièce).");
        }

        var alertText = Clean(row.Get(ImportColumn.AlertThreshold));
        if (alertText is not null && (!TryQuantity(alertText, unit, out alert) || alert > int.MaxValue))
        {
            Fail(ImportColumn.AlertThreshold, unit == MeasureUnit.Piece
                ? "l'alerte stock doit être un nombre entier, 0 ou plus."
                : "l'alerte stock doit être un nombre de 0 ou plus, avec au plus 3 chiffres après la virgule.");
        }

        var stockText = Clean(row.Get(ImportColumn.InitialStock));
        if (stockText is not null && (!TryQuantity(stockText, unit, out stock) || stock > int.MaxValue))
        {
            Fail(ImportColumn.InitialStock, unit == MeasureUnit.Piece
                ? "le stock de départ doit être un nombre entier, 0 ou plus."
                : "le stock de départ doit être un nombre de 0 ou plus, avec au plus 3 chiffres après la virgule.");
        }

        var tracks = true;
        var trackText = Clean(row.Get(ImportColumn.TracksStock));
        if (trackText is not null && !TryYesNo(trackText, out tracks))
        {
            Fail(ImportColumn.TracksStock, "le suivi du stock doit être « Oui » ou « Non ».");
        }
        else if (!tracks && stock > 0)
        {
            Fail(ImportColumn.InitialStock, "ce produit n'a pas de suivi du stock : retirez le stock de départ ou mettez « Oui » au suivi du stock.");
        }

        return ok ? new Parsed(row, name!, label, category, barcode, purchase, sale, (int)alert, (int)stock, tracks, unit) : null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryAmount(string text, out long amount)
    {
        amount = 0;
        var cleaned = Currency().Replace(text.Trim(), string.Empty);
        cleaned = Spaces().Replace(cleaned, string.Empty);
        var match = WholeNumber().Match(cleaned);
        return match.Success && long.TryParse(match.Groups[1].Value, out amount);
    }

    /// <summary>Une pièce se compte en entier ; un produit au poids ou à la mesure accepte jusqu'à 3 décimales (1,250 kg), gardées en grammes ou millilitres.</summary>
    private static bool TryQuantity(string text, MeasureUnit unit, out long quantity)
    {
        if (unit == MeasureUnit.Piece)
        {
            return TryAmount(text, out quantity);
        }

        quantity = 0;
        var cleaned = Spaces().Replace(text.Trim(), string.Empty);
        var match = Decimal().Match(cleaned);
        if (!match.Success)
        {
            return false;
        }

        var fraction = match.Groups[2].Value.PadRight(3, '0');
        quantity = (long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 1000) + long.Parse(fraction, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryUnit(string text, out MeasureUnit unit)
    {
        unit = MeasureUnit.Piece;
        switch (TextKey.Compact(text))
        {
            case "piece" or "pieces" or "pc" or "unite" or "unites" or "u":
                return true;
            case "kg" or "kilo" or "kilos" or "kilogramme" or "kilogrammes":
                unit = MeasureUnit.Kilogram;
                return true;
            case "l" or "litre" or "litres":
                unit = MeasureUnit.Litre;
                return true;
            default:
                return false;
        }
    }

    private static bool TryYesNo(string text, out bool value)
    {
        value = true;
        switch (TextKey.Fold(text.Trim()))
        {
            case "oui" or "o" or "1" or "vrai" or "true" or "yes" or "y" or "x":
                value = true;
                return true;
            case "non" or "n" or "0" or "faux" or "false" or "no":
                value = false;
                return true;
            default:
                return false;
        }
    }

    [GeneratedRegex(@"^(\d{1,9})(?:[.,](\d{1,3}))?$")]
    private static partial Regex Decimal();

    [GeneratedRegex(@"(?i)\s*(f\s*cfa|xof|f)\s*$")]
    private static partial Regex Currency();

    [GeneratedRegex(@"[\s  ]")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(\d{1,12})(?:[.,]0+)?$")]
    private static partial Regex WholeNumber();

    [GeneratedRegex(@"^\d+(?:[.,]\d+)?[eE][+-]?\d+$")]
    private static partial Regex ScientificNotation();
}
