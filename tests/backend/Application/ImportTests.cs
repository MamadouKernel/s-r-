using Soro.Application.Common;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Stock;

namespace Soro.Application.Tests;

/// <summary>Une ligne du fichier, dans l'ordre des colonnes du modèle (nom, format, catégorie, code-barres, achat, vente, alerte, stock, suivi).</summary>
internal static class Row
{
    public static ImportRow At(int line, string? name, string? format = null, string? category = null, string? barcode = null,
        string? buy = "0", string? sell = "100", string? alert = null, string? stock = null, string? track = null) =>
        new(line, new Dictionary<ImportColumn, string?>
        {
            [ImportColumn.Name] = name, [ImportColumn.Format] = format, [ImportColumn.Category] = category, [ImportColumn.Barcode] = barcode,
            [ImportColumn.PurchasePrice] = buy, [ImportColumn.SalePrice] = sell, [ImportColumn.AlertThreshold] = alert,
            [ImportColumn.InitialStock] = stock, [ImportColumn.TracksStock] = track,
        });
}

public class ImportPlannerTests
{
    private static readonly CatalogueSnapshot Empty = new([], new HashSet<string>());

    private static ImportPlan Plan(IEnumerable<ImportRow> rows, CatalogueSnapshot? catalogue = null) =>
        ProductImportPlanner.Plan(rows.ToArray(), catalogue ?? Empty);

    private static string Texts(ImportPlan plan) => string.Join(" | ", plan.Problems.Select(problem => $"{problem.Line}:{problem.Message}"));

    [Fact]
    public void Rows_with_the_same_name_form_one_family_with_one_format_each()
    {
        var plan = Plan([
            Row.At(2, "Riz parfumé", "5 kg", "Épicerie", buy: "2400", sell: "3200", alert: "5"),
            Row.At(3, "riz  PARFUME", "25 kg", "Épicerie", buy: "11 500", sell: "14500 FCFA"),
            Row.At(4, "Sel fin", sell: "200", stock: "40"),
        ]);

        Assert.Empty(plan.Problems);
        Assert.Equal(2, plan.NewFamilies.Count);
        var rice = plan.NewFamilies.Single(family => family.Name == "Riz parfumé");
        Assert.Equal("Épicerie", rice.Category);
        Assert.Equal(["5 kg", "25 kg"], rice.Formats.Select(format => format.Label).ToArray());
        Assert.Equal([2400L, 11500L], rice.Formats.Select(format => format.PurchasePriceFcfa).ToArray());
        Assert.Equal(14500, rice.Formats[1].SalePriceFcfa);
        Assert.Equal(5, rice.Formats[0].AlertThreshold);
        var salt = plan.NewFamilies.Single(family => family.Name == "Sel fin");
        Assert.Null(salt.Formats[0].Label);
        Assert.Equal(40, salt.Formats[0].InitialStock);
        Assert.True(salt.Formats[0].TracksStock);
        Assert.Equal(3, plan.RowsRead);
        Assert.Equal(3, plan.NewFormats);
    }

    [Theory]
    [InlineData("1500", 1500)]
    [InlineData("1 500", 1500)]
    [InlineData("1 500 FCFA", 1500)]
    [InlineData("1500 F", 1500)]
    [InlineData("1500,0", 1500)]
    [InlineData("1500.00", 1500)]
    [InlineData("0", 0)]
    public void Amounts_are_read_the_way_people_type_them(string typed, long expected)
    {
        var plan = Plan([Row.At(2, "Jus", sell: typed)]);

        Assert.Empty(plan.Problems);
        Assert.Equal(expected, plan.NewFamilies[0].Formats[0].SalePriceFcfa);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12,5")]
    [InlineData("-3")]
    [InlineData("1e3")]
    [InlineData("1 2 abc")]
    public void An_amount_that_is_not_a_whole_number_is_refused_in_plain_french(string typed)
    {
        var plan = Plan([Row.At(5, "Jus", sell: typed)]);

        Assert.Equal(1, plan.ProblemCount);
        Assert.Equal(5, plan.Problems[0].Line);
        Assert.Equal("Prix de vente", plan.Problems[0].Column);
        Assert.Contains("nombre entier", plan.Problems[0].Message, StringComparison.Ordinal);
        Assert.Empty(plan.NewFamilies);
    }

    [Fact]
    public void The_sale_price_is_required_and_the_purchase_price_defaults_to_zero()
    {
        var missing = Plan([Row.At(2, "Jus", sell: null)]);
        var defaulted = Plan([Row.At(2, "Jus", buy: null, sell: "300")]);

        Assert.Contains("prix de vente", missing.Problems[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(defaulted.Problems);
        Assert.Equal(0, defaulted.NewFamilies[0].Formats[0].PurchasePriceFcfa);
    }

    [Fact]
    public void A_name_is_required_and_blank_lines_are_ignored()
    {
        var plan = Plan([Row.At(2, "  ", sell: "100"), Row.At(3, null, null, null, null, null, null, null, null, null)]);

        Assert.Equal(1, plan.ProblemCount);
        Assert.Equal("Nom du produit", plan.Problems[0].Column);
        Assert.Equal(1, plan.RowsRead);
    }

    [Fact]
    public void Text_longer_than_the_limits_is_refused()
    {
        var plan = Plan([Row.At(2, new string('a', 201)), Row.At(3, "Eau", new string('x', 41)), Row.At(4, "Pain", category: new string('c', 101)), Row.At(5, "Lait", barcode: new string('9', 65))]);

        Assert.Equal(4, plan.ProblemCount);
    }

    [Fact]
    public void Several_rows_of_a_family_need_a_format_each_and_formats_cannot_repeat()
    {
        var missing = Plan([Row.At(2, "Eau", "1 L"), Row.At(3, "Eau")]);
        var repeated = Plan([Row.At(2, "Eau", "1 L"), Row.At(3, "eau", "1 l"), Row.At(4, "Eau", " 1   L ")]);

        Assert.Contains("format", missing.Problems.Single().Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, missing.Problems.Single().Line);
        Assert.Equal([3, 4], repeated.Problems.Select(problem => problem.Line).ToArray());
        Assert.All(repeated.Problems, problem => Assert.Contains("1 L", problem.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void Two_different_categories_in_the_same_family_are_reported_but_an_empty_one_is_fine()
    {
        var conflict = Plan([Row.At(2, "Eau", "1 L", "Boissons"), Row.At(3, "Eau", "2 L", "Eaux")]);
        var empty = Plan([Row.At(2, "Eau", "1 L", "Boissons"), Row.At(3, "Eau", "2 L")]);

        Assert.Equal(3, conflict.Problems.Single().Line);
        Assert.Equal("Catégorie", conflict.Problems.Single().Column);
        Assert.Empty(empty.Problems);
        Assert.Equal("Boissons", empty.NewFamilies[0].Category);
    }

    [Fact]
    public void A_barcode_must_be_unique_in_the_file_and_in_the_shop()
    {
        var existing = new CatalogueSnapshot(
            [new ExistingFamily(FamilyId.New(), "Autre", null, [new ExistingFormat(null, "999")])], new HashSet<string> { "999" });

        var inFile = Plan([Row.At(2, "A", barcode: "111"), Row.At(3, "B", barcode: "111")]);
        var inShop = Plan([Row.At(2, "Savon", barcode: "999")], existing);

        Assert.Equal(3, inFile.Problems.Single().Line);
        Assert.Contains("ligne 2", inFile.Problems.Single().Message, StringComparison.Ordinal);
        Assert.Empty(inShop.Problems);
        Assert.Equal(1, inShop.Skipped.Count);
        Assert.Equal(2, inShop.Skipped[0].Line);
    }

    [Theory]
    [InlineData("6.111E+12")]
    [InlineData("6,1112E12")]
    public void A_barcode_that_excel_turned_into_a_scientific_number_is_reported_not_guessed(string typed)
    {
        var plan = Plan([Row.At(2, "Jus", barcode: typed)]);

        Assert.Equal("Code-barres", plan.Problems.Single().Column);
        Assert.Contains("Texte", plan.Problems.Single().Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Oui", true)]
    [InlineData("oui", true)]
    [InlineData("", true)]
    [InlineData("Non", false)]
    [InlineData("0", false)]
    [InlineData("faux", false)]
    [InlineData("1", true)]
    public void Stock_tracking_reads_yes_and_no(string typed, bool expected)
    {
        var plan = Plan([Row.At(2, "Coupe", track: typed)]);

        Assert.Empty(plan.Problems);
        Assert.Equal(expected, plan.NewFamilies[0].Formats[0].TracksStock);
    }

    [Fact]
    public void An_unknown_stock_tracking_word_and_a_starting_stock_without_tracking_are_refused()
    {
        var word = Plan([Row.At(2, "Coupe", track: "peut-être")]);
        var service = Plan([Row.At(2, "Coupe", track: "Non", stock: "5")]);

        Assert.Equal("Suivi du stock", word.Problems.Single().Column);
        Assert.Equal("Stock de départ", service.Problems.Single().Column);
    }

    [Fact]
    public void A_row_already_in_the_catalogue_is_skipped_not_reported_as_a_problem()
    {
        var family = FamilyId.New();
        var existing = new CatalogueSnapshot(
            [new ExistingFamily(family, "Riz parfumé", "Épicerie", [new ExistingFormat("5 kg", null), new ExistingFormat("25 kg", null)])], new HashSet<string>());

        var plan = Plan([Row.At(2, "riz parfumé", "5 KG"), Row.At(3, "Riz parfumé", "25 kg")], existing);

        Assert.Empty(plan.Problems);
        Assert.Empty(plan.NewFamilies);
        Assert.Empty(plan.Additions);
        Assert.Equal(2, plan.Skipped.Count);
        Assert.False(plan.CanImport);
    }

    [Fact]
    public void A_new_format_of_an_existing_family_with_named_formats_is_added_to_it()
    {
        var family = FamilyId.New();
        var existing = new CatalogueSnapshot(
            [new ExistingFamily(family, "Riz parfumé", "Épicerie", [new ExistingFormat("5 kg", null), new ExistingFormat("25 kg", null)])], new HashSet<string>());

        var plan = Plan([Row.At(2, "Riz parfumé", "1 kg", sell: "800"), Row.At(3, "Riz parfumé", "5 kg")], existing);

        Assert.Empty(plan.Problems);
        var addition = Assert.Single(plan.Additions);
        Assert.Equal(family, addition.Family);
        Assert.Equal("1 kg", addition.Format.Label);
        Assert.Single(plan.Skipped);
        Assert.Equal(1, plan.NewFormats);
        Assert.True(plan.CanImport);
    }

    [Fact]
    public void A_new_format_for_an_existing_product_without_format_sends_the_user_to_its_sheet()
    {
        var existing = new CatalogueSnapshot(
            [new ExistingFamily(FamilyId.New(), "Sel fin", null, [new ExistingFormat(null, null)])], new HashSet<string>());

        var plan = Plan([Row.At(2, "Sel fin", "500 g")], existing);

        Assert.Equal(2, plan.Problems.Single().Line);
        Assert.Contains("fiche", plan.Problems.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_problem_is_counted_but_only_fifty_are_listed()
    {
        var plan = Plan(Enumerable.Range(2, 80).Select(line => Row.At(line, "Produit " + line, sell: "abc")));

        Assert.Equal(80, plan.ProblemCount);
        Assert.Equal(50, plan.Problems.Count);
        Assert.Equal(2, plan.Problems[0].Line);
    }

    [Fact]
    public void Problems_come_in_line_order_and_nothing_is_planned_when_there_is_one()
    {
        var plan = Plan([Row.At(2, "Bon", sell: "100"), Row.At(3, "Mauvais", sell: "x"), Row.At(4, "Bon 2", sell: "100")]);

        Assert.False(plan.CanImport);
        Assert.Empty(plan.NewFamilies);
        Assert.Equal(0, plan.NewFormats);
    }

    [Fact]
    public void Messages_are_in_plain_french_without_technical_words()
    {
        var plan = Plan([Row.At(2, "", sell: "x", alert: "y", stock: "z", buy: "w")]);

        var all = string.Join(" ", plan.Problems.Select(problem => problem.Message));
        Assert.DoesNotContain("null", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guid", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", all, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, plan.ProblemCount);
    }
}

public class ImportColumnTests
{
    [Theory]
    [InlineData("Nom du produit", ImportColumn.Name)]
    [InlineData("  NOM DU PRODUIT ", ImportColumn.Name)]
    [InlineData("Catégorie", ImportColumn.Category)]
    [InlineData("categorie", ImportColumn.Category)]
    [InlineData("Code-barres", ImportColumn.Barcode)]
    [InlineData("code barres", ImportColumn.Barcode)]
    [InlineData("Prix d'achat", ImportColumn.PurchasePrice)]
    [InlineData("Prix d’achat", ImportColumn.PurchasePrice)]
    [InlineData("Prix de vente", ImportColumn.SalePrice)]
    [InlineData("Alerte stock", ImportColumn.AlertThreshold)]
    [InlineData("Stock de départ", ImportColumn.InitialStock)]
    [InlineData("Suivi du stock", ImportColumn.TracksStock)]
    [InlineData("Format", ImportColumn.Format)]
    public void A_header_is_recognised_without_regard_to_case_accents_or_apostrophes(string header, ImportColumn expected) =>
        Assert.Equal(expected, ImportColumns.Match(header));

    [Fact]
    public void An_unknown_header_is_not_a_column() => Assert.Null(ImportColumns.Match("Couleur"));
}
