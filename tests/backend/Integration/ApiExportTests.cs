using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Soro.Application.Export;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Customers;
using Soro.Domain.Identity;
using Soro.Domain.Sales;
using Soro.Domain.Stock;
using Soro.Domain.Tenancy;
using Soro.Infrastructure.Export;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

/// <summary>Le classeur lui-même : sept feuilles, vrais nombres et vraies dates, aucune formule, aucun identifiant technique (docs/specs/export-donnees).</summary>
public class ExportWriterTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 15, 8, 5, 0, TimeSpan.Zero);

    private static DataExport Sample() => new(
        "Boutique Awa",
        new ExportPeriod(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)),
        new DateTimeOffset(2026, 10, 25, 17, 30, 0, TimeSpan.Zero),
        [
            new ProductExportRow("Eau minérale", "1 L", "Boissons", "6111000000011", 180, 300, 3, 5, true, true),
            new ProductExportRow("Ancien produit", null, null, null, 0, 100, 0, 0, false, false),
            new ProductExportRow("Riz en vrac", null, null, null, 450, 600, 12_500, 2_250, true, true, MeasureUnit.Kilogram),
        ],
        [new CustomerExportRow("=1+1", "+225 07 07 07 07 07", 20_000, 1_500, true)],
        [new RepaymentExportRow(At, "=1+1", 500, PaymentMethod.Wave, "W-1", "Awa Koné")],
        [
            new SaleExportRow("15/10/2026 08:05 · ticket 7 · Awa Koné", At, 7, "Awa Koné", null, 650, 50, 600, [PaymentMethod.Cash, PaymentMethod.Wave], SaleStatus.Validated, null),
            new SaleExportRow("15/10/2026 09:00 · ticket 8 · Awa Koné", At.AddMinutes(55), 8, "Awa Koné", "=1+1", 300, 0, 300, [PaymentMethod.Credit], SaleStatus.Cancelled, "-Erreur de caisse"),
        ],
        [
            new SaleLineExportRow("15/10/2026 08:05 · ticket 7 · Awa Koné", At, 7, "Eau minérale · 1 L", 2, 300, 600, 180),
            new SaleLineExportRow("15/10/2026 08:05 · ticket 7 · Awa Koné", At, 7, "Savon", 1, 50, 50, null),
            new SaleLineExportRow("15/10/2026 08:05 · ticket 7 · Awa Koné", At, 7, "Riz en vrac", 1_250, 600, 750, 450, MeasureUnit.Kilogram),
        ],
        [new MovementExportRow(At, "Eau minérale · 1 L", StockMovementKind.Reception, 24, "Grossiste Koné", 180, "Awa Koné")]);

    private static XLWorkbook Open(DataExport data) => new(new MemoryStream(new ClosedXmlDataExportWriter().Write(data)));

    private static string[] Headers(IXLWorksheet sheet) =>
        sheet.Row(1).CellsUsed().Select(cell => cell.GetString()).ToArray();

    [Fact]
    public void The_workbook_has_seven_french_sheets_each_with_its_titles()
    {
        using var workbook = Open(Sample());

        Assert.Equal(["Lisez-moi", "Produits", "Clients", "Versements", "Ventes", "Lignes de vente", "Mouvements de stock"], workbook.Worksheets.Select(sheet => sheet.Name).ToArray());
        Assert.Equal(["Produit", "Format", "Catégorie", "Code-barres", "Prix d'achat", "Prix de vente", "Stock", "Alerte stock", "Suivi du stock", "État", "Unité"], Headers(workbook.Worksheet("Produits")));
        Assert.Equal(["Nom", "Téléphone", "Plafond de crédit", "Dette", "État"], Headers(workbook.Worksheet("Clients")));
        Assert.Equal(["Date et heure", "Client", "Montant", "Moyen", "Référence", "Encaissé par"], Headers(workbook.Worksheet("Versements")));
        Assert.Equal(
            ["Vente", "Date et heure", "N° de ticket", "Caissier", "Client", "Total brut", "Remise", "Total net", "Moyens de paiement", "État", "Motif d'annulation"],
            Headers(workbook.Worksheet("Ventes")));
        Assert.Equal(["Vente", "Date et heure", "N° de ticket", "Produit", "Quantité", "Prix unitaire", "Total", "Coût unitaire retenu", "Unité"], Headers(workbook.Worksheet("Lignes de vente")));
        Assert.Equal(["Date et heure", "Produit", "Type", "Quantité", "Fournisseur ou motif", "Coût unitaire", "Fait par", "Unité"], Headers(workbook.Worksheet("Mouvements de stock")));
    }

    [Fact]
    public void Products_show_the_family_and_the_format_apart_with_real_numbers_and_the_state_in_words()
    {
        using var workbook = Open(Sample());
        var sheet = workbook.Worksheet("Produits");

        Assert.Equal("Eau minérale", sheet.Cell(2, 1).GetString());
        Assert.Equal("1 L", sheet.Cell(2, 2).GetString());
        Assert.Equal("6111000000011", sheet.Cell(2, 4).GetString());
        Assert.Equal(180d, sheet.Cell(2, 5).GetDouble());
        Assert.Equal(300d, sheet.Cell(2, 6).GetDouble());
        Assert.Equal("Oui", sheet.Cell(2, 9).GetString());
        Assert.Equal("Actif", sheet.Cell(2, 10).GetString());
        Assert.Equal("Non", sheet.Cell(3, 9).GetString());
        Assert.Equal("Désactivé", sheet.Cell(3, 10).GetString());
        Assert.True(sheet.Cell(3, 2).IsEmpty());
    }

    [Fact]
    public void Dates_are_real_excel_dates_and_amounts_are_real_numbers()
    {
        using var workbook = Open(Sample());
        var sales = workbook.Worksheet("Ventes");

        Assert.Equal(new DateTime(2026, 10, 15, 8, 5, 0), sales.Cell(2, 2).GetDateTime());
        Assert.Equal("dd/mm/yyyy hh:mm", sales.Cell(2, 2).Style.DateFormat.Format);
        Assert.Equal(600d, sales.Cell(2, 8).GetDouble());
        Assert.Equal("#,##0", sales.Cell(2, 8).Style.NumberFormat.Format);
        Assert.Equal(7d, sales.Cell(2, 3).GetDouble());
    }

    [Fact]
    public void A_sale_lists_its_payment_methods_in_french_and_a_cancelled_one_shows_its_state_and_reason()
    {
        using var workbook = Open(Sample());
        var sales = workbook.Worksheet("Ventes");

        Assert.Equal("Espèces, Wave", sales.Cell(2, 9).GetString());
        Assert.Equal("Validée", sales.Cell(2, 10).GetString());
        Assert.Equal("Crédit", sales.Cell(3, 9).GetString());
        Assert.Equal("Annulée", sales.Cell(3, 10).GetString());
        Assert.Equal("-Erreur de caisse", sales.Cell(3, 11).GetString());
    }

    [Fact]
    public void A_line_links_to_its_sale_by_the_readable_key_and_an_unknown_cost_stays_empty_not_zero()
    {
        using var workbook = Open(Sample());
        var lines = workbook.Worksheet("Lignes de vente");

        Assert.Equal("15/10/2026 08:05 · ticket 7 · Awa Koné", lines.Cell(2, 1).GetString());
        Assert.Equal(workbook.Worksheet("Ventes").Cell(2, 1).GetString(), lines.Cell(2, 1).GetString());
        Assert.Equal(180d, lines.Cell(2, 8).GetDouble());
        Assert.True(lines.Cell(3, 8).IsEmpty());
    }

    [Fact]
    public void Weighed_quantities_are_written_in_kilos_with_their_unit_never_in_grams()
    {
        using var workbook = Open(Sample());

        var products = workbook.Worksheet("Produits");
        Assert.Equal(12.5d, products.Cell(4, 7).GetDouble());
        Assert.Equal(2.25d, products.Cell(4, 8).GetDouble());
        Assert.Equal("kg", products.Cell(4, 11).GetString());
        Assert.Equal("pièce", products.Cell(2, 11).GetString());
        var lines = workbook.Worksheet("Lignes de vente");
        Assert.Equal(1.25d, lines.Cell(4, 5).GetDouble());
        Assert.Equal(750d, lines.Cell(4, 7).GetDouble());
        Assert.Equal("kg", lines.Cell(4, 9).GetString());
    }

    [Fact]
    public void Movements_and_repayments_are_named_in_french()
    {
        using var workbook = Open(Sample());

        Assert.Equal("Réception", workbook.Worksheet("Mouvements de stock").Cell(2, 3).GetString());
        Assert.Equal(24d, workbook.Worksheet("Mouvements de stock").Cell(2, 4).GetDouble());
        Assert.Equal("Wave", workbook.Worksheet("Versements").Cell(2, 4).GetString());
        Assert.Equal("Awa Koné", workbook.Worksheet("Versements").Cell(2, 6).GetString());
    }

    [Fact]
    public void A_text_that_looks_like_a_formula_stays_a_text_everywhere()
    {
        using var workbook = Open(Sample());

        var formulas = workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed()).Where(cell => cell.HasFormula).ToList();
        Assert.Empty(formulas);
        Assert.Equal("=1+1", workbook.Worksheet("Clients").Cell(2, 1).GetString());
        Assert.Equal("+225 07 07 07 07 07", workbook.Worksheet("Clients").Cell(2, 2).GetString());
        Assert.Equal("-Erreur de caisse", workbook.Worksheet("Ventes").Cell(3, 11).GetString());
        Assert.Equal("=1+1", workbook.Worksheet("Ventes").Cell(3, 5).GetString());
    }

    [Fact]
    public void An_empty_sheet_keeps_its_titles_and_every_sheet_has_a_frozen_title_row_and_a_filter()
    {
        var empty = Sample() with { Repayments = [], Movements = [] };
        using var workbook = Open(empty);

        Assert.Equal(["Date et heure", "Client", "Montant", "Moyen", "Référence", "Encaissé par"], Headers(workbook.Worksheet("Versements")));
        Assert.True(workbook.Worksheet("Versements").Cell(2, 1).IsEmpty());
        foreach (var sheet in workbook.Worksheets.Where(sheet => sheet.Name != "Lisez-moi"))
        {
            Assert.Equal(1, sheet.SheetView.SplitRow);
            Assert.True(sheet.AutoFilter.IsEnabled, sheet.Name);
        }
    }

    [Fact]
    public void The_readme_names_the_shop_the_period_the_counts_and_warns_about_personal_data()
    {
        using var workbook = Open(Sample());
        var text = string.Join("\n", workbook.Worksheet("Lisez-moi").CellsUsed().Select(cell => cell.GetString()));

        Assert.Contains("Boutique Awa", text, StringComparison.Ordinal);
        Assert.Contains("25/10/2026 à 17:30", text, StringComparison.Ordinal);
        Assert.Contains("du 01/10/2026 au 31/10/2026", text, StringComparison.Ordinal);
        Assert.Contains("Ventes : 2 lignes", text, StringComparison.Ordinal);
        Assert.Contains("Lignes de vente : 3 lignes", text, StringComparison.Ordinal);
        Assert.Contains("Côte d'Ivoire", text, StringComparison.Ordinal);
        Assert.Contains("numéros de téléphone de vos clients", text, StringComparison.Ordinal);
    }

    [Fact]
    public void No_cell_of_the_workbook_holds_a_technical_identifier()
    {
        using var workbook = Open(Sample());
        var guid = new Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

        Assert.DoesNotContain(workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed()), cell => guid.IsMatch(cell.GetString()));
    }
}

/// <summary>L'export de bout en bout : vraies données, vraie base, droits, abonnement, limite, journal et isolation.</summary>
[Collection(Postgres.Collection)]
public sealed class ApiExportTests(PostgresFixture fixture) : IDisposable
{
    private static readonly DateTimeOffset Day = Samples.Morning.AddMinutes(5);
    private static readonly Regex Guid = new("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    private const string Range = "from=2026-10-01&to=2026-10-31";

    private readonly ApiFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private HttpClient As(TenantId tenant, Role role) => _factory.ClientFor(tenant, UserId.New(), role);

    /// <summary>Une boutique avec un caissier, un produit à deux formats dont un désactivé, un client, une vente, un versement et une réception.</summary>
    private async Task<TenantId> ShopAsync(string name = "Boutique Export", int createdDaysAgo = 5, string productName = "Eau minérale")
    {
        var tenant = TenantId.New();
        await using var db = Db.Owner(fixture);
        await new CommerceRepository(db).AddAsync(
            Commerce.Open(tenant, name, ActivityType.Shop, EmailAddress.Parse($"{System.Guid.NewGuid():N}@exemple.ci"), "v1", DateTimeOffset.UtcNow.AddDays(-createdDaysAgo)),
            CancellationToken.None);
        var cashier = User.Register(UserId.New(), tenant, $"awa.{System.Guid.NewGuid():N}"[..12], "Awa Koné", Role.Cashier, "hash", Samples.Morning);
        await new UserRepository(db).AddAsync(cashier, CancellationToken.None);

        var family = ProductFamily.Create(FamilyId.New(), tenant, productName, "Boissons", Samples.Morning);
        await new ProductFamilyRepository(db).AddAsync(family, CancellationToken.None);
        var litre = Product.Create(ProductId.New(), tenant, $"{productName} · 1 L", "6111000000011", "Boissons", Money.Fcfa(180), Money.Fcfa(300), 5, true, Samples.Morning, family.Id, "1 L");
        var movement = litre.Receive(24, "Grossiste Koné", cashier.Id, Samples.Morning, null, Money.Fcfa(180));
        var old = Product.Create(ProductId.New(), tenant, $"{productName} · 50 cl", null, "Boissons", Money.Fcfa(80), Money.Fcfa(150), 0, true, Samples.Morning, family.Id, "50 cl");
        old.Deactivate();
        await new ProductRepository(db).AddAsync(litre, CancellationToken.None);
        await new ProductRepository(db).AddAsync(old, CancellationToken.None);
        await new StockMovementRepository(db).AddAsync(movement, CancellationToken.None);

        var customer = Customer.Create(CustomerId.New(), tenant, "Fatou Diallo", "07 07 07 07 07", Money.Fcfa(20_000), Samples.Morning);
        await new CustomerRepository(db).AddAsync(customer, CancellationToken.None);
        await new DebtRepaymentRepository(db).AddAsync(
            DebtRepayment.Record(DebtRepaymentId.New(), tenant, customer.Id, Money.Fcfa(500), PaymentMethod.Cash, null, cashier.Id, Day), CancellationToken.None);

        var session = CashRegisterSession.Open(SessionId.New(), new RegisterAssignment(tenant, PointOfSaleId.New(), RegisterId.New(), cashier.Id), Money.Fcfa(10_000), Samples.Morning);
        var sale = session.RecordSale(new SaleDraft(
            SaleId.New(), 7, Day, [SaleLine.Create(litre.Id, litre.Name, 2, Money.Fcfa(300), Money.Fcfa(180))], Money.Zero,
            [Payment.Cash(PaymentId.New(), Money.Fcfa(600), Money.Fcfa(600))], null));
        await new SessionRepository(db).AddAsync(session, CancellationToken.None);
        await new SaleRepository(db).AddAsync(sale, CancellationToken.None);
        await new EfUnitOfWork(db).CommitAsync(CancellationToken.None);
        return tenant;
    }

    private static async Task<XLWorkbook> WorkbookAsync(HttpResponseMessage response) =>
        new(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));

    /// <summary>Les lignes de données d'une feuille (sans la ligne des titres), chaque cellule lue comme du texte.</summary>
    private static string[][] Table(XLWorkbook workbook, string sheet)
    {
        var worksheet = workbook.Worksheet(sheet);
        var columns = worksheet.Row(1).CellsUsed().Count();
        return worksheet.RowsUsed().Skip(1)
            .Select(row => Enumerable.Range(1, columns).Select(column => row.Cell(column).GetString()).ToArray())
            .ToArray();
    }

    [Fact]
    public async Task The_owner_downloads_a_workbook_with_the_real_data_of_the_shop_and_nothing_technical()
    {
        var tenant = await ShopAsync();
        using var owner = As(tenant, Role.Owner);

        var response = await owner.GetAsync($"/api/v1/data-export?{Range}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Matches(@"^soro-export-\d{4}-\d{2}-\d{2}\.xlsx$", disposition.FileNameStar ?? disposition.FileName ?? string.Empty);
        using var workbook = await WorkbookAsync(response);

        var products = Table(workbook, "Produits");
        Assert.Equal(2, products.Length);
        Assert.Contains(products, row => row[0] == "Eau minérale" && row[1] == "1 L" && row[9] == "Actif" && row[6] == "24");
        Assert.Contains(products, row => row[1] == "50 cl" && row[9] == "Désactivé");
        var customers = Table(workbook, "Clients");
        Assert.Equal(["Fatou Diallo", "+2250707070707", "20000", "0", "Actif"], customers[0]);
        var sales = Table(workbook, "Ventes");
        Assert.Equal("15/10/2026 08:05 · ticket 7 · Awa Koné", sales[0][0]);
        Assert.Equal("Espèces", sales[0][8]);
        Assert.Equal("Validée", sales[0][9]);
        var lines = Table(workbook, "Lignes de vente");
        Assert.Equal("Eau minérale · 1 L", lines[0][3]);
        Assert.Equal("180", lines[0][7]);
        var movements = Table(workbook, "Mouvements de stock");
        Assert.Equal("Réception", movements[0][2]);
        Assert.Equal("Grossiste Koné", movements[0][4]);
        Assert.Equal("Awa Koné", movements[0][6]);
        var repayments = Table(workbook, "Versements");
        Assert.Equal("Fatou Diallo", repayments[0][1]);
        Assert.Equal("Espèces", repayments[0][3]);
        Assert.DoesNotContain(workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed()), cell => Guid.IsMatch(cell.GetString()));
    }

    [Fact]
    public async Task The_period_filters_the_history_but_never_the_products_and_the_customers()
    {
        var tenant = await ShopAsync();
        using var owner = As(tenant, Role.Owner);

        using var empty = await WorkbookAsync(await owner.GetAsync("/api/v1/data-export?from=2026-01-01&to=2026-01-31"));
        using var edge = await WorkbookAsync(await owner.GetAsync("/api/v1/data-export?from=2026-10-15&to=2026-10-15"));

        Assert.Empty(Table(empty, "Ventes"));
        Assert.Empty(Table(empty, "Mouvements de stock"));
        Assert.Equal(2, Table(empty, "Produits").Length);
        Assert.Single(Table(empty, "Clients"));
        Assert.Single(Table(edge, "Ventes")); // le dernier jour est compris en entier
    }

    [Fact]
    public async Task Another_shop_never_appears_in_the_file()
    {
        var tenant = await ShopAsync();
        await ShopAsync("Autre boutique", productName: "Produit secret");
        using var owner = As(tenant, Role.Owner);

        using var workbook = await WorkbookAsync(await owner.GetAsync($"/api/v1/data-export?{Range}"));

        var everything = string.Join("|", workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed()).Select(cell => cell.GetString()));
        Assert.DoesNotContain("Produit secret", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("Autre boutique", everything, StringComparison.Ordinal);
        Assert.Equal(2, Table(workbook, "Produits").Length);
    }

    [Fact]
    public async Task Only_the_owner_exports()
    {
        var tenant = await ShopAsync();
        foreach (var role in new[] { Role.Manager, Role.Cashier, Role.StockKeeper, Role.Accountant })
        {
            using var client = As(tenant, role);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/data-export?{Range}")).StatusCode);
        }
    }

    [Fact]
    public async Task It_still_works_when_the_subscription_is_over_and_the_shop_is_read_only()
    {
        var tenant = await ShopAsync(createdDaysAgo: 90);
        using var owner = As(tenant, Role.Owner);

        var state = await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscription");
        var export = await owner.GetAsync($"/api/v1/data-export?{Range}");

        Assert.Equal("ReadOnly", state.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
    }

    [Fact]
    public async Task A_bad_period_is_refused_in_plain_words()
    {
        var tenant = await ShopAsync();
        using var owner = As(tenant, Role.Owner);

        var reversed = await owner.GetAsync("/api/v1/data-export?from=2026-10-31&to=2026-10-01");
        var tooLong = await owner.GetAsync("/api/v1/data-export?from=2019-01-01&to=2026-10-01");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reversed.StatusCode);
        Assert.Contains("début", (await reversed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);
        Assert.Contains("5 ans", (await tooLong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_export_is_written_in_the_journal_and_the_sixth_of_the_hour_is_refused_without_a_trace()
    {
        var tenant = await ShopAsync();
        using var owner = As(tenant, Role.Owner);

        for (var index = 0; index < 5; index++)
        {
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/data-export?{Range}")).StatusCode);
        }

        var sixth = await owner.GetAsync($"/api/v1/data-export?{Range}");
        var journal = await owner.GetFromJsonAsync<JsonElement>("/api/v1/audit-log?category=data");

        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        Assert.Contains("réessayez dans", (await sixth.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(), StringComparison.Ordinal);
        var entries = journal.GetProperty("entries").EnumerateArray().Where(entry => entry.GetProperty("action").GetString() == "data.exported").ToArray();
        Assert.Equal(5, entries.Length);
        Assert.Contains("1 ventes", entries[0].GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_export_leaves_no_trace_and_does_not_use_up_the_hourly_quota()
    {
        var tenant = await ShopAsync();
        using var owner = As(tenant, Role.Owner);
        using var manager = As(tenant, Role.Manager);

        await manager.GetAsync($"/api/v1/data-export?{Range}");
        await owner.GetAsync("/api/v1/data-export?from=2026-10-31&to=2026-10-01");
        var journal = await owner.GetFromJsonAsync<JsonElement>("/api/v1/audit-log?category=data");

        Assert.Equal(0, journal.GetProperty("entries").GetArrayLength());
    }
}
