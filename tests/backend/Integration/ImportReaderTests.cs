using System.IO.Compression;
using ClosedXML.Excel;
using Soro.Application.Stock;
using Soro.Infrastructure.Import;

namespace Soro.Integration.Tests;

public class ImportReaderTests
{
    private static readonly ClosedXmlProductSheetReader Reader = new();

    [Fact]
    public void The_template_is_a_workbook_the_reader_accepts_with_the_right_titles_and_no_example_line()
    {
        var bytes = new ClosedXmlProductTemplateWriter().Write();

        var read = Reader.Read(bytes);

        Assert.Null(read.FileProblem);
        Assert.Empty(read.Rows);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["Produits", "Mode d'emploi"], workbook.Worksheets.Select(sheet => sheet.Name).ToArray());
        Assert.Equal(ImportFiles.Headers, ImportColumns.All.Select(column => workbook.Worksheet("Produits").Cell(1, (int)column + 1).GetString()).ToArray());
        Assert.Equal("@", workbook.Worksheet("Produits").Column(4).Style.NumberFormat.Format);
    }

    [Fact]
    public void Rows_are_read_with_their_excel_line_number_and_numbers_as_plain_text()
    {
        var read = Reader.Read(ImportFiles.Workbook(ImportFiles.RiceAndSalt()));

        Assert.Null(read.FileProblem);
        Assert.Equal([2, 3, 4], read.Rows.Select(row => row.Line).ToArray());
        var first = read.Rows[0];
        Assert.Equal("Riz parfumé", first.Get(ImportColumn.Name));
        Assert.Equal("6111000000011", first.Get(ImportColumn.Barcode));
        Assert.Equal("2400", first.Get(ImportColumn.PurchasePrice));
        Assert.Equal("14500", read.Rows[1].Get(ImportColumn.SalePrice));
        Assert.Null(read.Rows[1].Get(ImportColumn.Barcode));
        Assert.Null(read.Rows[2].Get(ImportColumn.Format));
    }

    [Fact]
    public void Titles_are_found_by_name_whatever_the_order_the_case_the_accents_and_extra_columns()
    {
        var headers = new[] { "PRIX DE VENTE", "Couleur", "nom du produit", "Catégorie" };
        var rows = new List<object?[]> { new object?[] { 300.0, "rouge", "Jus", "Boissons" } };

        var read = Reader.Read(ImportFiles.Workbook(rows, headers));

        Assert.Null(read.FileProblem);
        var row = Assert.Single(read.Rows);
        Assert.Equal("Jus", row.Get(ImportColumn.Name));
        Assert.Equal("300", row.Get(ImportColumn.SalePrice));
        Assert.Equal("Boissons", row.Get(ImportColumn.Category));
    }

    [Fact]
    public void A_missing_required_column_is_named_in_plain_french()
    {
        var read = Reader.Read(ImportFiles.Workbook([], ["Nom du produit", "Catégorie"]));

        Assert.Contains("« Prix de vente »", read.FileProblem, StringComparison.Ordinal);
        Assert.Empty(read.Rows);
    }

    [Fact]
    public void The_sheet_named_produits_is_preferred_to_the_first_one()
    {
        using var workbook = new XLWorkbook();
        workbook.Worksheets.Add("Notes").Cell(1, 1).SetValue("rien d'utile");
        var sheet = workbook.Worksheets.Add("Produits");
        sheet.Cell(1, 1).SetValue("Nom du produit");
        sheet.Cell(1, 2).SetValue("Prix de vente");
        sheet.Cell(2, 1).SetValue("Jus");
        sheet.Cell(2, 2).SetValue(300.0);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var read = Reader.Read(stream.ToArray());

        Assert.Equal("Jus", Assert.Single(read.Rows).Get(ImportColumn.Name));
    }

    [Fact]
    public void A_formula_is_never_calculated_its_text_is_kept_and_so_is_a_text_that_looks_like_one()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Produits");
        sheet.Cell(1, 1).SetValue("Nom du produit");
        sheet.Cell(1, 2).SetValue("Prix de vente");
        sheet.Cell(2, 1).FormulaA1 = "1+1";
        sheet.Cell(2, 2).SetValue(100.0);
        sheet.Cell(3, 1).SetValue("=HYPERLINK(\"http://exemple.test\")");
        sheet.Cell(3, 2).SetValue(100.0);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var read = Reader.Read(stream.ToArray());

        Assert.Equal("=1+1", read.Rows[0].Get(ImportColumn.Name));
        Assert.Equal("=HYPERLINK(\"http://exemple.test\")", read.Rows[1].Get(ImportColumn.Name));
    }

    [Fact]
    public void A_decimal_amount_keeps_its_decimals_so_that_the_check_can_refuse_it()
    {
        var read = Reader.Read(ImportFiles.Workbook([["Jus", null, null, null, null, 12.5, null, null, null]]));

        Assert.Equal("12.5", read.Rows[0].Get(ImportColumn.SalePrice));
    }

    [Fact]
    public void Bytes_that_are_not_a_workbook_give_a_plain_message_not_an_exception()
    {
        Assert.NotNull(Reader.Read([1, 2, 3, 4, 5]).FileProblem);
        Assert.NotNull(Reader.Read("Nom du produit;Prix de vente\nJus;300"u8.ToArray()).FileProblem);

        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("notes.txt").Open());
            writer.Write("pas un classeur");
        }

        Assert.NotNull(Reader.Read(zip.ToArray()).FileProblem);
    }

    [Fact]
    public void A_small_file_that_opens_into_something_huge_is_refused_without_being_opened()
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize).Open();
            var block = new byte[1024 * 1024];
            for (var index = 0; index < 60; index++)
            {
                entry.Write(block);
            }
        }

        var read = Reader.Read(zip.ToArray());

        Assert.True(zip.Length < 2 * 1024 * 1024);
        Assert.Contains("trop volumineux", read.FileProblem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sheet_with_far_too_many_lines_is_refused()
    {
        var rows = Enumerable.Range(0, 2300).Select(index => new object?[] { "P" + index, null, null, null, null, 100.0 }).ToList();

        var read = Reader.Read(ImportFiles.Workbook(rows));

        Assert.Contains("2 000", read.FileProblem, StringComparison.Ordinal);
    }
}
