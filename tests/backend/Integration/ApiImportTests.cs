using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Integration.Tests;

/// <summary>Importation des produits depuis Excel, de bout en bout (docs/specs/import-excel).</summary>
[Collection(Postgres.Collection)]
public sealed class ApiImportTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private HttpClient As(Role role, TenantId? tenant = null) => _factory.ClientFor(tenant ?? _tenant, UserId.New(), role);

    private static ByteArrayContent File(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return content;
    }

    private static Task<HttpResponseMessage> Preview(HttpClient client, byte[] bytes) => client.PostAsync("/api/v1/product-imports/preview", File(bytes));

    private static Task<HttpResponseMessage> Import(HttpClient client, byte[] bytes, Guid? id = null) =>
        client.PostAsync($"/api/v1/product-imports?importId={id ?? Guid.NewGuid()}", File(bytes));

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task The_manager_downloads_the_template_as_an_xlsx_file()
    {
        using var manager = As(Role.Manager);

        var response = await manager.GetAsync("/api/v1/product-imports/template");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("modele-import-produits.xlsx", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0x50, 0x4B }, bytes[..2]);
    }

    [Fact]
    public async Task A_valid_file_is_previewed_then_imported_with_families_formats_stock_and_cost()
    {
        using var manager = As(Role.Manager);
        var file = ImportFiles.Workbook(ImportFiles.RiceAndSalt());

        var preview = await Json(await Preview(manager, file));
        Assert.Equal(2, preview.GetProperty("newProducts").GetInt32());
        Assert.Equal(3, preview.GetProperty("newFormats").GetInt32());
        Assert.True(preview.GetProperty("canImport").GetBoolean());
        Assert.Equal(0, (await manager.GetFromJsonAsync<JsonElement>("/api/v1/products")).GetArrayLength());

        var imported = await Import(manager, file);

        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var report = await Json(imported);
        Assert.True(report.GetProperty("imported").GetBoolean());
        Assert.Equal(3, report.GetProperty("stockMovements").GetInt32());
        var catalogue = (await manager.GetFromJsonAsync<JsonElement>("/api/v1/products")).EnumerateArray().ToArray();
        Assert.Equal(["Riz parfumé · 25 kg", "Riz parfumé · 5 kg", "Sel fin"], catalogue.Select(product => product.GetProperty("name").GetString()!).Order().ToArray());
        var big = catalogue.Single(product => product.GetProperty("formatLabel").GetString() == "25 kg");
        Assert.Equal("Épicerie", big.GetProperty("category").GetString());
        Assert.Equal(14500, big.GetProperty("salePriceFcfa").GetInt64());
        Assert.Equal(6, big.GetProperty("quantityOnHand").GetInt32());
        Assert.Equal(11500, big.GetProperty("purchasePriceFcfa").GetInt64());
        Assert.Equal("Riz parfumé", big.GetProperty("familyName").GetString());
        var history = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/products/{big.GetProperty("id").GetGuid()}/movements");
        Assert.Equal("Reception", Assert.Single(history.EnumerateArray()).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task One_wrong_line_is_a_422_with_every_problem_listed_and_nothing_is_created()
    {
        using var manager = As(Role.Manager);
        var file = ImportFiles.Workbook([
            ["Bon", null, null, null, null, 100.0],
            ["Mauvais", null, null, null, null, "abc"],
            ["Autre", null, null, null, null, null],
        ]);

        var response = await Import(manager, file);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var report = await Json(response);
        Assert.False(report.GetProperty("imported").GetBoolean());
        Assert.Equal(2, report.GetProperty("problemCount").GetInt32());
        var problems = report.GetProperty("problems").EnumerateArray().ToArray();
        Assert.Equal([3, 4], problems.Select(problem => problem.GetProperty("line").GetInt32()).ToArray());
        Assert.StartsWith("Ligne 3 :", problems[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, (await manager.GetFromJsonAsync<JsonElement>("/api/v1/products")).GetArrayLength());
    }

    [Fact]
    public async Task Replaying_an_import_creates_nothing_more_and_importing_the_same_file_again_skips_everything()
    {
        using var manager = As(Role.Manager);
        var file = ImportFiles.Workbook(ImportFiles.RiceAndSalt());
        var id = Guid.NewGuid();
        await Import(manager, file, id);

        var replay = await Json(await Import(manager, file, id));
        var again = await Json(await Import(manager, file));

        Assert.True(replay.GetProperty("alreadyProcessed").GetBoolean());
        Assert.False(again.GetProperty("alreadyProcessed").GetBoolean());
        Assert.Equal(3, again.GetProperty("skippedCount").GetInt32());
        Assert.False(again.GetProperty("imported").GetBoolean());
        Assert.Equal(3, (await manager.GetFromJsonAsync<JsonElement>("/api/v1/products")).GetArrayLength());
    }

    [Fact]
    public async Task A_barcode_of_another_shop_is_no_conflict_but_one_of_this_shop_skips_the_line()
    {
        using var other = As(Role.Manager, TenantId.New());
        await Import(other, ImportFiles.Workbook([["Jus", null, null, "123", null, 300.0]]));
        using var manager = As(Role.Manager);

        var first = await Json(await Import(manager, ImportFiles.Workbook([["Jus", null, null, "123", null, 300.0]])));
        var second = await Json(await Import(manager, ImportFiles.Workbook([["Autre jus", null, null, "123", null, 300.0]])));

        Assert.True(first.GetProperty("imported").GetBoolean());
        Assert.False(second.GetProperty("imported").GetBoolean());
        Assert.Equal(1, second.GetProperty("skippedCount").GetInt32());
    }

    [Fact]
    public async Task Only_a_manager_may_use_the_template_the_preview_and_the_import()
    {
        var file = ImportFiles.Workbook(ImportFiles.RiceAndSalt());
        foreach (var role in new[] { Role.Cashier, Role.StockKeeper, Role.Accountant })
        {
            using var client = As(role);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/product-imports/template")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Preview(client, file)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Import(client, file)).StatusCode);
        }
    }

    [Fact]
    public async Task A_csv_an_old_xls_a_huge_file_and_a_missing_identifier_are_refused_with_a_clear_message()
    {
        using var manager = As(Role.Manager);

        var csv = await Preview(manager, "Nom du produit;Prix de vente\nJus;300"u8.ToArray());
        var xls = await Preview(manager, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);
        var big = new byte[3 * 1024 * 1024];
        new byte[] { 0x50, 0x4B, 0x03, 0x04 }.CopyTo(big, 0);
        var huge = await Preview(manager, big);
        var noId = await manager.PostAsync("/api/v1/product-imports", File(ImportFiles.Workbook(ImportFiles.RiceAndSalt())));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, csv.StatusCode);
        Assert.Contains(".xlsx", (await Json(csv)).GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, xls.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, huge.StatusCode);
        Assert.Contains("trop lourd", (await Json(huge)).GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, noId.StatusCode);
    }

    [Fact]
    public async Task A_formula_in_the_name_is_imported_as_plain_text()
    {
        using var manager = As(Role.Manager);

        await Import(manager, ImportFiles.Workbook([["=1+1", null, null, null, null, 100.0]]));

        var catalogue = await manager.GetFromJsonAsync<JsonElement>("/api/v1/products");
        Assert.Equal("=1+1", Assert.Single(catalogue.EnumerateArray()).GetProperty("name").GetString());
    }
}
