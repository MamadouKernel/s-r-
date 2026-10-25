using System.Globalization;
using ClosedXML.Excel;
using Soro.Application.Export;

namespace Soro.Infrastructure.Export;

/// <summary>
/// Fabrique le classeur d'export (docs/specs/export-donnees DON-02, DON-06) : sept feuilles en français, vraies dates et vrais nombres, première ligne
/// figée, filtre sur chaque feuille. Valeurs seulement : un texte qui commence par « = », « + », « - » ou « @ » reste un texte, jamais une formule.
/// </summary>
public sealed class ClosedXmlDataExportWriter : IDataExportWriter
{
    private const string DateFormat = "dd/mm/yyyy hh:mm";
    private const string MoneyFormat = "#,##0";

    public byte[] Write(DataExport data)
    {
        using var workbook = new XLWorkbook();
        ReadMe(workbook.Worksheets.Add("Lisez-moi"), data);

        Table(workbook, "Produits", [("Produit", 30), ("Format", 14), ("Catégorie", 18), ("Code-barres", 18), ("Prix d'achat", 14), ("Prix de vente", 14), ("Stock", 10), ("Alerte stock", 12), ("Suivi du stock", 14), ("État", 12)],
            data.Products, (sheet, row, item) =>
            {
                Text(sheet, row, 1, item.Name);
                Text(sheet, row, 2, item.Format);
                Text(sheet, row, 3, item.Category);
                Text(sheet, row, 4, item.Barcode);
                Money(sheet, row, 5, item.PurchasePriceFcfa);
                Money(sheet, row, 6, item.SalePriceFcfa);
                sheet.Cell(row, 7).SetValue(item.Stock);
                sheet.Cell(row, 8).SetValue(item.Threshold);
                Text(sheet, row, 9, item.TracksStock ? "Oui" : "Non");
                Text(sheet, row, 10, item.IsActive ? "Actif" : "Désactivé");
            });

        Table(workbook, "Clients", [("Nom", 28), ("Téléphone", 18), ("Plafond de crédit", 18), ("Dette", 14), ("État", 12)], data.Customers, (sheet, row, item) =>
        {
            Text(sheet, row, 1, item.Name);
            Text(sheet, row, 2, item.Phone);
            Money(sheet, row, 3, item.CreditCeilingFcfa);
            Money(sheet, row, 4, item.DebtFcfa);
            Text(sheet, row, 5, item.IsActive ? "Actif" : "Désactivé");
        });

        Table(workbook, "Versements", [("Date et heure", 18), ("Client", 26), ("Montant", 14), ("Moyen", 16), ("Référence", 20), ("Encaissé par", 22)], data.Repayments, (sheet, row, item) =>
        {
            Date(sheet, row, 1, item.At);
            Text(sheet, row, 2, item.Customer);
            Money(sheet, row, 3, item.AmountFcfa);
            Text(sheet, row, 4, ExportLabels.Method(item.Method));
            Text(sheet, row, 5, item.Reference);
            Text(sheet, row, 6, item.By);
        });

        Table(workbook, "Ventes", [("Vente", 42), ("Date et heure", 18), ("N° de ticket", 12), ("Caissier", 22), ("Client", 24), ("Total brut", 14), ("Remise", 12), ("Total net", 14), ("Moyens de paiement", 26), ("État", 12), ("Motif d'annulation", 30)],
            data.Sales, (sheet, row, item) =>
            {
                Text(sheet, row, 1, item.Key);
                Date(sheet, row, 2, item.At);
                sheet.Cell(row, 3).SetValue(item.Ticket);
                Text(sheet, row, 4, item.Cashier);
                Text(sheet, row, 5, item.Customer);
                Money(sheet, row, 6, item.GrossFcfa);
                Money(sheet, row, 7, item.DiscountFcfa);
                Money(sheet, row, 8, item.NetFcfa);
                Text(sheet, row, 9, string.Join(", ", item.Methods.Select(ExportLabels.Method)));
                Text(sheet, row, 10, ExportLabels.Status(item.Status));
                Text(sheet, row, 11, item.CancelReason);
            });

        Table(workbook, "Lignes de vente", [("Vente", 42), ("Date et heure", 18), ("N° de ticket", 12), ("Produit", 30), ("Quantité", 10), ("Prix unitaire", 14), ("Total", 14), ("Coût unitaire retenu", 18)],
            data.SaleLines, (sheet, row, item) =>
            {
                Text(sheet, row, 1, item.Key);
                Date(sheet, row, 2, item.At);
                sheet.Cell(row, 3).SetValue(item.Ticket);
                Text(sheet, row, 4, item.Product);
                sheet.Cell(row, 5).SetValue(item.Quantity);
                Money(sheet, row, 6, item.UnitPriceFcfa);
                Money(sheet, row, 7, item.TotalFcfa);
                if (item.UnitCostFcfa is { } cost)
                {
                    Money(sheet, row, 8, cost);
                }
            });

        Table(workbook, "Mouvements de stock", [("Date et heure", 18), ("Produit", 30), ("Type", 20), ("Quantité", 10), ("Fournisseur ou motif", 30), ("Coût unitaire", 14), ("Fait par", 22)],
            data.Movements, (sheet, row, item) =>
            {
                Date(sheet, row, 1, item.At);
                Text(sheet, row, 2, item.Product);
                Text(sheet, row, 3, ExportLabels.Movement(item.Kind));
                sheet.Cell(row, 4).SetValue(item.QuantityDelta);
                Text(sheet, row, 5, item.Reason);
                if (item.UnitCostFcfa is { } cost)
                {
                    Money(sheet, row, 6, cost);
                }

                Text(sheet, row, 7, item.By);
            });

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void ReadMe(IXLWorksheet sheet, DataExport data)
    {
        var lines = new[]
        {
            $"Export des données — {data.CommerceName}",
            string.Empty,
            $"Fichier fabriqué le {data.At.UtcDateTime.ToString("dd/MM/yyyy à HH:mm", CultureInfo.InvariantCulture)}. Toutes les heures sont celles de la Côte d'Ivoire (identiques à l'heure UTC : le pays n'a pas d'heure d'été).",
            $"Période de l'historique : du {data.Period.From.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} au {data.Period.To.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} (versements, ventes, lignes de vente, mouvements de stock).",
            "Les produits et les clients sont toujours complets : ce sont des états à ce jour, pas des historiques.",
            "Les montants sont en FCFA.",
            string.Empty,
            "Contenu :",
            $"  Produits : {data.Products.Count} lignes",
            $"  Clients : {data.Customers.Count} lignes",
            $"  Versements : {data.Repayments.Count} lignes",
            $"  Ventes : {data.Sales.Count} lignes",
            $"  Lignes de vente : {data.SaleLines.Count} lignes",
            $"  Mouvements de stock : {data.Movements.Count} lignes",
            string.Empty,
            "ATTENTION : ce fichier contient les numéros de téléphone de vos clients et vos prix d'achat. Gardez-le en lieu sûr.",
        };
        sheet.Column(1).Width = 120;
        for (var index = 0; index < lines.Length; index++)
        {
            var cell = sheet.Cell(index + 1, 1);
            cell.SetValue(lines[index]);
            cell.Style.Alignment.WrapText = true;
        }

        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(lines.Length, 1).Style.Font.Bold = true;
    }

    private static void Table<T>(XLWorkbook workbook, string name, (string Title, double Width)[] columns, IReadOnlyList<T> rows, Action<IXLWorksheet, int, T> fill)
    {
        var sheet = workbook.Worksheets.Add(name);
        for (var column = 0; column < columns.Length; column++)
        {
            var header = sheet.Cell(1, column + 1);
            header.SetValue(columns[column].Title);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDE2DA");
            sheet.Column(column + 1).Width = columns[column].Width;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            fill(sheet, index + 2, rows[index]);
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, rows.Count + 1, columns.Length).SetAutoFilter();
    }

    private static void Text(IXLWorksheet sheet, int row, int column, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            sheet.Cell(row, column).SetValue(value);
        }
    }

    private static void Money(IXLWorksheet sheet, int row, int column, long value)
    {
        var cell = sheet.Cell(row, column);
        cell.SetValue(value);
        cell.Style.NumberFormat.Format = MoneyFormat;
    }

    private static void Date(IXLWorksheet sheet, int row, int column, DateTimeOffset value)
    {
        var cell = sheet.Cell(row, column);
        cell.SetValue(value.UtcDateTime);
        cell.Style.DateFormat.Format = DateFormat;
    }
}
