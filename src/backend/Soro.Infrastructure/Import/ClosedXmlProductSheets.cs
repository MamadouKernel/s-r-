using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using Soro.Application.Stock;

namespace Soro.Infrastructure.Import;

/// <summary>
/// Lit un classeur .xlsx avec ClosedXML (docs/specs/import-excel IMP-02, IMP-08). Le fichier est une donnée non fiable : taille une fois ouvert bornée,
/// nombre de lignes borné, valeurs seulement. Une formule n'est jamais calculée : on garde son texte tel quel, précédé de « = ».
/// </summary>
public sealed class ClosedXmlProductSheetReader : IProductSheetReader
{
    private const long MaxOpenedBytes = 50L * 1024 * 1024;
    private const int MaxEntries = 300;
    private const int MaxHeaderSearchRows = 10;
    private const int MaxColumns = 60;
    private const int MaxRows = 2000;
    private const string Template = " Utilisez le modèle à télécharger.";

    public SheetRead Read(byte[] file)
    {
        if (!IsReasonableOnceOpened(file))
        {
            return Problem("Ce fichier est trop volumineux une fois ouvert. Enlevez les images et les feuilles inutiles.");
        }

        try
        {
            using var stream = new MemoryStream(file, writable: false);
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.FirstOrDefault(candidate => TextKey.Compact(candidate.Name) == "produits") ?? workbook.Worksheets.FirstOrDefault();
            if (sheet is null)
            {
                return Problem("Ce classeur ne contient aucune feuille." + Template);
            }

            var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
            var (headerRow, columns) = FindHeader(sheet, Math.Min(last, MaxHeaderSearchRows));
            if (headerRow == 0)
            {
                return Problem("Je ne trouve pas la ligne des titres (« Nom du produit », « Prix de vente »…)." + Template);
            }

            var missing = new[] { ImportColumn.Name, ImportColumn.SalePrice }.Where(column => !columns.ContainsValue(column)).ToArray();
            if (missing.Length > 0)
            {
                return Problem($"Il manque la colonne {string.Join(" et la colonne ", missing.Select(column => "« " + ImportColumns.Title(column) + " »"))}." + Template);
            }

            if (last - headerRow > MaxRows + 200)
            {
                return Problem("Ce fichier a plus de 2 000 lignes. Coupez-le en plusieurs fichiers.");
            }

            var rows = new List<ImportRow>();
            for (var number = headerRow + 1; number <= last; number++)
            {
                var cells = new Dictionary<ImportColumn, string?>();
                foreach (var (columnNumber, column) in columns)
                {
                    cells[column] = Text(sheet.Cell(number, columnNumber));
                }

                rows.Add(new ImportRow(number, cells));
            }

            return new SheetRead(null, rows);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Problem("Ce fichier n'a pas pu être lu : il est abîmé ou protégé." + Template);
        }
    }

    private static SheetRead Problem(string message) => new(message, []);

    /// <summary>Un .xlsx est un zip : on regarde ce qu'il donnerait une fois ouvert avant de le confier à la bibliothèque.</summary>
    private static bool IsReasonableOnceOpened(byte[] file)
    {
        try
        {
            using var stream = new MemoryStream(file, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            return archive.Entries.Count <= MaxEntries && archive.Entries.Sum(entry => entry.Length) <= MaxOpenedBytes;
        }
        catch (InvalidDataException)
        {
            return true; // pas un zip lisible : la lecture donnera le message « abîmé »
        }
    }

    private static (int Row, Dictionary<int, ImportColumn> Columns) FindHeader(IXLWorksheet sheet, int searchRows)
    {
        for (var row = 1; row <= searchRows; row++)
        {
            var columns = new Dictionary<int, ImportColumn>();
            var seen = new HashSet<ImportColumn>();
            for (var column = 1; column <= MaxColumns; column++)
            {
                var title = Text(sheet.Cell(row, column));
                if (title is not null && ImportColumns.Match(title) is { } match && seen.Add(match))
                {
                    columns[column] = match;
                }
            }

            if (columns.ContainsValue(ImportColumn.Name))
            {
                return (row, columns);
            }
        }

        return (0, []);
    }

    private static string? Text(IXLCell cell)
    {
        if (cell.HasFormula)
        {
            return "=" + cell.FormulaA1;
        }

        var value = cell.Value;
        if (value.IsBlank)
        {
            return null;
        }

        if (value.IsNumber)
        {
            var number = value.GetNumber();
            return number == Math.Floor(number) && Math.Abs(number) < 1e15
                ? ((long)number).ToString(CultureInfo.InvariantCulture)
                : number.ToString("R", CultureInfo.InvariantCulture);
        }

        if (value.IsText)
        {
            return value.GetText();
        }

        if (value.IsBoolean)
        {
            return value.GetBoolean() ? "Oui" : "Non";
        }

        if (value.IsDateTime)
        {
            return value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>Le classeur modèle : une feuille « Produits » avec les titres, une feuille « Mode d'emploi » avec des exemples.</summary>
public sealed class ClosedXmlProductTemplateWriter : IProductTemplateWriter
{
    public byte[] Write()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Produits");
        foreach (var column in ImportColumns.All)
        {
            var number = (int)column + 1;
            var header = sheet.Cell(1, number);
            header.Value = ImportColumns.Title(column);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDE2DA");
            sheet.Column(number).Width = column == ImportColumn.Name ? 30 : 16;
        }

        // Un code-barres est du texte : en nombre, Excel le raccourcit (6,1E+12) ou perd ses zéros de tête.
        sheet.Column((int)ImportColumn.Barcode + 1).Style.NumberFormat.Format = "@";
        sheet.SheetView.FreezeRows(1);

        var guide = workbook.Worksheets.Add("Mode d'emploi");
        guide.Column(1).Width = 110;
        var lines = new[]
        {
            "Comment remplir la feuille « Produits »",
            string.Empty,
            "• Une ligne = un produit (ou un format d'un produit). Ne changez pas les titres des colonnes.",
            "• Plusieurs lignes avec le même « Nom du produit » forment un seul produit en plusieurs formats (5 kg, 25 kg…).",
            "• « Format » : 1 L, 50 kg, Pack de 6… Facultatif pour un produit seul, obligatoire dès qu'un produit a plusieurs lignes.",
            "• « Prix d'achat », « Prix de vente », « Alerte stock », « Stock de départ » : des nombres entiers, sans virgule. Le prix de vente est obligatoire.",
            "• « Stock de départ » : la quantité que vous avez aujourd'hui. Elle est enregistrée comme une première réception.",
            "• « Suivi du stock » : Oui (par défaut) ou Non pour une prestation sans stock (coupe, réparation…).",
            "• « Unité » (facultatif) : pièce (par défaut), kg ou litre. Pour un produit vendu au poids ou à la mesure, le prix est celui d'un kg ou d'un litre, et « Alerte stock » et « Stock de départ » peuvent avoir une virgule (12,5).",
            "• Un produit déjà dans votre catalogue n'est jamais modifié : sa ligne est simplement ignorée.",
            string.Empty,
            "Exemple (à ne pas recopier tel quel) :",
            "Nom du produit | Format | Catégorie | Code-barres | Prix d'achat | Prix de vente | Alerte stock | Stock de départ | Suivi du stock | Unité",
            "Riz parfumé | 5 kg | Épicerie | 6111000000011 | 2400 | 3200 | 5 | 20 | Oui | pièce",
            "Riz parfumé | 25 kg | Épicerie |  | 11500 | 14500 | 2 | 6 | Oui | pièce",
            "Sel fin |  | Épicerie |  | 120 | 200 | 5 | 40 | Oui | pièce",
            "Riz en vrac |  | Épicerie |  | 450 | 600 | 10 | 120,5 | Oui | kg",
        };
        for (var index = 0; index < lines.Length; index++)
        {
            var cell = guide.Cell(index + 1, 1);
            cell.SetValue(lines[index]);
            cell.Style.Alignment.WrapText = true;
        }

        guide.Cell(1, 1).Style.Font.Bold = true;
        guide.Cell(12, 1).Style.Font.Bold = true;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
