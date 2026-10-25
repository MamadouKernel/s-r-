using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Integration.Tests;

/// <summary>Familles de produits, formats et photo facultative, de bout en bout (docs/specs/formats-produits).</summary>
[Collection(Postgres.Collection)]
public sealed class ApiFamilyTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private HttpClient As(Role role, TenantId? tenant = null) => _factory.ClientFor(tenant ?? _tenant, UserId.New(), role);

    private static object Format(string? label, string? barcode, long sale, Guid? id = null) =>
        new { productId = id ?? Guid.NewGuid(), label, barcode, purchasePriceFcfa = sale / 2, salePriceFcfa = sale, alertThreshold = 3, tracksStock = true };

    private static object Water(Guid? family = null, params object[] formats) =>
        new
        {
            familyId = family ?? Guid.NewGuid(),
            name = "Eau minérale",
            category = "Boissons",
            formats = formats.Length > 0 ? formats : [Format("1,5 L", "111", 400), Format("1 L", "222", 300), Format("30 cl", "333", 150)],
        };

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    private static ByteArrayContent Image(byte[] data, string contentType = "image/jpeg")
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }

    private static byte[] Jpeg(int size = 3_000)
    {
        var data = new byte[size];
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(data, 0);
        return data;
    }

    [Fact]
    public async Task A_manager_creates_water_with_three_formats_and_the_catalogue_lists_each_one()
    {
        using var manager = As(Role.Manager);
        var family = Guid.NewGuid();

        var created = await manager.PostAsJsonAsync("/api/v1/product-families", Water(family));

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var catalogue = await manager.GetFromJsonAsync<JsonElement>("/api/v1/products?search=eau");
        Assert.Equal(3, catalogue.GetArrayLength());
        var litre = catalogue.EnumerateArray().Single(product => product.GetProperty("formatLabel").GetString() == "1 L");
        Assert.Equal("Eau minérale · 1 L", litre.GetProperty("name").GetString());
        Assert.Equal("Eau minérale", litre.GetProperty("familyName").GetString());
        Assert.Equal(family, litre.GetProperty("familyId").GetGuid());
        Assert.Equal(300, litre.GetProperty("salePriceFcfa").GetInt64());
        Assert.Equal(JsonValueKind.Null, litre.GetProperty("photoId").ValueKind);
    }

    [Fact]
    public async Task The_search_finds_a_format_by_its_label_and_a_barcode_scan_finds_exactly_that_format()
    {
        using var manager = As(Role.Manager);
        await manager.PostAsJsonAsync("/api/v1/product-families", Water());

        var byLabel = await manager.GetFromJsonAsync<JsonElement>("/api/v1/products?search=1,5");
        var byBarcode = await manager.GetFromJsonAsync<JsonElement>("/api/v1/products?search=333");

        Assert.Equal("Eau minérale · 1,5 L", Assert.Single(byLabel.EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal("Eau minérale · 30 cl", Assert.Single(byBarcode.EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Replaying_the_creation_of_a_family_changes_nothing()
    {
        using var manager = As(Role.Manager);
        var body = Water();
        await manager.PostAsJsonAsync("/api/v1/product-families", body);

        var replay = await manager.PostAsJsonAsync("/api/v1/product-families", body);

        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyProcessed").GetBoolean());
        Assert.Equal(3, (await manager.GetFromJsonAsync<JsonElement>("/api/v1/products")).GetArrayLength());
    }

    [Fact]
    public async Task A_duplicate_label_is_a_422_and_a_used_barcode_a_409_and_nothing_is_created()
    {
        using var manager = As(Role.Manager);

        await AssertProblemAsync(
            await manager.PostAsJsonAsync("/api/v1/product-families", Water(null, Format("1 L", "1", 300), Format("1 l", "2", 310))),
            HttpStatusCode.UnprocessableEntity,
            "rule_violation");
        await manager.PostAsJsonAsync("/api/v1/product-families", Water(null, Format(null, "555", 100)));
        await AssertProblemAsync(
            await manager.PostAsJsonAsync("/api/v1/product-families", Water(null, Format("2 L", "555", 100))),
            HttpStatusCode.Conflict,
            "barcode_taken");

        Assert.Equal(1, (await manager.GetFromJsonAsync<JsonElement>("/api/v1/products")).GetArrayLength());
    }

    [Fact]
    public async Task Adding_a_format_renaming_the_family_and_relabelling_a_format_keep_the_names_in_step()
    {
        using var manager = As(Role.Manager);
        var family = Guid.NewGuid();
        var jug = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/product-families", Water(family, Format("1 L", "1", 300, jug)));

        var added = await manager.PostAsJsonAsync(
            $"/api/v1/product-families/{family}/formats", new { format = Format("5 L", "2", 900), labelForExistingFormat = (string?)null });
        var renamed = await manager.PutAsJsonAsync($"/api/v1/product-families/{family}", new { name = "Eau de source", category = "Eaux" });
        var relabelled = await manager.PutAsJsonAsync(
            $"/api/v1/products/{jug}",
            new { name = "Eau de source", formatLabel = "1 litre", barcode = "1", category = "Eaux", purchasePriceFcfa = 150, salePriceFcfa = 320, alertThreshold = 3 });

        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, relabelled.StatusCode);
        var names = (await manager.GetFromJsonAsync<JsonElement>("/api/v1/products")).EnumerateArray()
            .Select(product => product.GetProperty("name").GetString()!).Order().ToArray();
        Assert.Equal(["Eau de source · 1 litre", "Eau de source · 5 L"], names);
    }

    [Fact]
    public async Task Existing_products_are_grouped_into_a_family_without_changing_their_stock()
    {
        using var manager = As(Role.Manager);
        var big = Guid.NewGuid();
        var small = Guid.NewGuid();
        foreach (var (id, name, barcode) in new[] { (big, "Eau 1,5 L", "1"), (small, "Eau 50 cl", "2") })
        {
            await manager.PostAsJsonAsync("/api/v1/products", new { productId = id, name, barcode, category = "Boissons", purchasePriceFcfa = 100, salePriceFcfa = 200, alertThreshold = 0, tracksStock = true });
        }

        await manager.PostAsJsonAsync($"/api/v1/products/{big}/receptions", new { movementId = Guid.NewGuid(), quantity = 12 });

        var grouped = await manager.PostAsJsonAsync(
            "/api/v1/product-families/group",
            new { familyId = Guid.NewGuid(), name = "Eau minérale", category = "Boissons", members = new[] { new { productId = big, label = "1,5 L" }, new { productId = small, label = "50 cl" } } });

        Assert.Equal(HttpStatusCode.OK, grouped.StatusCode);
        var catalogue = (await manager.GetFromJsonAsync<JsonElement>("/api/v1/products")).EnumerateArray().ToArray();
        Assert.Equal(["Eau minérale · 1,5 L", "Eau minérale · 50 cl"], catalogue.Select(product => product.GetProperty("name").GetString()!).Order().ToArray());
        Assert.Equal(12, catalogue.Single(product => product.GetProperty("id").GetGuid() == big).GetProperty("quantityOnHand").GetInt32());
    }

    [Fact]
    public async Task A_cashier_reads_formats_without_the_purchase_price_and_changes_nothing()
    {
        using var manager = As(Role.Manager);
        var family = Guid.NewGuid();
        var format = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/product-families", Water(family, Format("1 L", "1", 300, format)));
        using var cashier = As(Role.Cashier);

        var catalogue = await cashier.GetFromJsonAsync<JsonElement>("/api/v1/products");
        Assert.Equal(JsonValueKind.Null, Assert.Single(catalogue.EnumerateArray()).GetProperty("purchasePriceFcfa").ValueKind);

        await AssertProblemAsync(await cashier.PostAsJsonAsync("/api/v1/product-families", Water()), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(
            await cashier.PutAsJsonAsync($"/api/v1/product-families/{family}", new { name = "X" }), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(
            await cashier.PutAsync($"/api/v1/product-families/{family}/photo", Image(Jpeg())), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(
            await cashier.PutAsync($"/api/v1/products/{format}/photo", Image(Jpeg())), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Another_shop_sees_neither_the_families_nor_can_it_change_them()
    {
        using var manager = As(Role.Manager);
        var family = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/product-families", Water(family));
        using var stranger = As(Role.Manager, TenantId.New());

        Assert.Equal(0, (await stranger.GetFromJsonAsync<JsonElement>("/api/v1/products")).GetArrayLength());
        await AssertProblemAsync(
            await stranger.PutAsJsonAsync($"/api/v1/product-families/{family}", new { name = "Volé" }), HttpStatusCode.NotFound, "not_found");
        await AssertProblemAsync(
            await stranger.PutAsync($"/api/v1/product-families/{family}/photo", Image(Jpeg())), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_photo_is_stored_served_privately_and_used_by_every_format_of_the_family()
    {
        using var manager = As(Role.Manager);
        var family = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/product-families", Water(family));
        var data = Jpeg(5_000);

        var put = await manager.PutAsync($"/api/v1/product-families/{family}/photo", Image(data));

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var photoId = (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photoId").GetGuid();
        var catalogue = await manager.GetFromJsonAsync<JsonElement>("/api/v1/products");
        Assert.All(catalogue.EnumerateArray(), product => Assert.Equal(photoId, product.GetProperty("photoId").GetGuid()));

        using var cashier = As(Role.Cashier);
        var served = await cashier.GetAsync($"/api/v1/photos/{photoId}");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/jpeg", served.Content.Headers.ContentType!.MediaType);
        Assert.Equal(data, await served.Content.ReadAsByteArrayAsync());
        Assert.Contains("private", served.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Contains("nosniff", served.Headers.GetValues("X-Content-Type-Options"));

        using var stranger = As(Role.Manager, TenantId.New());
        await AssertProblemAsync(await stranger.GetAsync($"/api/v1/photos/{photoId}"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_format_photo_replaces_the_family_one_and_removing_it_brings_the_family_photo_back()
    {
        using var manager = As(Role.Manager);
        var family = Guid.NewGuid();
        var format = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/product-families", Water(family, Format("1 L", "1", 300, format)));
        var familyPhoto = (await (await manager.PutAsync($"/api/v1/product-families/{family}/photo", Image(Jpeg()))).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("photoId").GetGuid();
        var ownPhoto = (await (await manager.PutAsync($"/api/v1/products/{format}/photo", Image(Jpeg(4_000)))).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("photoId").GetGuid();

        var own = (await manager.GetFromJsonAsync<JsonElement>("/api/v1/products"))[0].GetProperty("photoId").GetGuid();
        var removed = await manager.DeleteAsync($"/api/v1/products/{format}/photo");
        var back = (await manager.GetFromJsonAsync<JsonElement>("/api/v1/products"))[0].GetProperty("photoId").GetGuid();

        Assert.Equal(ownPhoto, own);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(familyPhoto, back);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/v1/photos/{ownPhoto}")).StatusCode);
    }

    [Fact]
    public async Task A_pdf_renamed_as_an_image_and_an_oversized_image_are_refused()
    {
        using var manager = As(Role.Manager);
        var family = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/product-families", Water(family));
        var pdf = new byte[3_000];
        "%PDF-1.7"u8.CopyTo(pdf);

        var notAnImage = await manager.PutAsync($"/api/v1/product-families/{family}/photo", Image(pdf));
        var tooBig = await manager.PutAsync($"/api/v1/product-families/{family}/photo", Image(Jpeg(2 * 1024 * 1024)));
        var limit = await manager.PutAsync($"/api/v1/product-families/{family}/photo", Image(Jpeg(200 * 1024)));

        await AssertProblemAsync(notAnImage, HttpStatusCode.UnprocessableEntity, "rule_violation");
        await AssertProblemAsync(tooBig, HttpStatusCode.UnprocessableEntity, "rule_violation");
        Assert.Equal(HttpStatusCode.OK, limit.StatusCode);
    }

    [Fact]
    public async Task A_family_without_photo_still_sells_normally()
    {
        using var manager = As(Role.Manager);
        await manager.PostAsJsonAsync("/api/v1/product-families", Water(null, Format(null, "9", 500)));

        var catalogue = await manager.GetFromJsonAsync<JsonElement>("/api/v1/products");

        var product = Assert.Single(catalogue.EnumerateArray());
        Assert.Equal(JsonValueKind.Null, product.GetProperty("photoId").ValueKind);
        Assert.Equal(500, product.GetProperty("salePriceFcfa").GetInt64());
    }
}
