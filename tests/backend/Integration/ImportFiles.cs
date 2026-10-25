using ClosedXML.Excel;
using Soro.Application.Stock;

namespace Soro.Integration.Tests;

/// <summary>Fabrique de classeurs .xlsx pour les tests d'import.</summary>
internal static class ImportFiles
{
    public static readonly string[] Headers =
        ["Nom du produit", "Format", "Catégorie", "Code-barres", "Prix d'achat", "Prix de vente", "Alerte stock", "Stock de départ", "Suivi du stock"];

    /// <summary>Un classeur « Produits » avec le modèle de colonnes et les lignes données (texte, ou nombre pour un <c>double</c>).</summary>
    public static byte[] Workbook(IEnumerable<object?[]> rows, string[]? headers = null, string sheetName = "Produits")
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        var titles = headers ?? Headers;
        for (var column = 0; column < titles.Length; column++)
        {
            sheet.Cell(1, column + 1).SetValue(titles[column]);
        }

        var line = 2;
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
            {
                switch (row[column])
                {
                    case null:
                        break;
                    case double number:
                        sheet.Cell(line, column + 1).SetValue(number);
                        break;
                    default:
                        sheet.Cell(line, column + 1).SetValue(row[column]!.ToString()!);
                        break;
                }
            }

            line++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static IEnumerable<object?[]> RiceAndSalt() =>
    [
        ["Riz parfumé", "5 kg", "Épicerie", "6111000000011", 2400.0, 3200.0, 5.0, 20.0, "Oui"],
        ["Riz parfumé", "25 kg", "Épicerie", null, 11500.0, 14500.0, 2.0, 6.0, "Oui"],
        ["Sel fin", null, "Épicerie", null, "120", "200", "5", "40", null],
    ];

    public static string Text(this ImportRow row, ImportColumn column) => row.Get(column) ?? "<vide>";
}
