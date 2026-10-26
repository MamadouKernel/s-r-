using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Stock;

namespace Soro.Integration.Tests;

/// <summary>Codes-barres internes de bout en bout (docs/specs/etiquettes-tickets ETQ-01) : API réelle, PostgreSQL réel.</summary>
[Collection(Postgres.Collection)]
public sealed class ApiInternalBarcodeTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private HttpClient As(Role role, TenantId? tenant = null) => _factory.ClientFor(tenant ?? _tenant, UserId.New(), role);

    private static async Task<Guid> CreateProductAsync(HttpClient client, string name, string? barcode = null)
    {
        var id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            "/api/v1/products",
            new { productId = id, name, barcode, category = "Divers", purchasePriceFcfa = 100, salePriceFcfa = 150, alertThreshold = 0, tracksStock = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return id;
    }

    private static async Task<string> BarcodeOfAsync(HttpClient client, Guid id)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/products");
        return list.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id).GetProperty("barcode").GetString()!;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_manager_creates_a_valid_internal_code_that_the_product_then_carries()
    {
        using var manager = As(Role.Manager);
        var id = await CreateProductAsync(manager, "Pain de mie");

        var response = await manager.PostAsync($"/api/v1/products/{id}/internal-barcode", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var code = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("barcode").GetString()!;
        Assert.StartsWith("29", code, StringComparison.Ordinal);
        Assert.True(InternalBarcode.IsValidEan13(code));
        Assert.Equal(code, await BarcodeOfAsync(manager, id));
    }

    [Fact]
    public async Task The_sequence_goes_up_and_a_product_with_a_code_is_refused()
    {
        using var manager = As(Role.Manager);
        var first = await CreateProductAsync(manager, "Pain");
        var second = await CreateProductAsync(manager, "Riz");
        var factory = await CreateProductAsync(manager, "Jus", "6111000000014");

        var one = (await (await manager.PostAsync($"/api/v1/products/{first}/internal-barcode", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("barcode").GetString()!;
        var two = (await (await manager.PostAsync($"/api/v1/products/{second}/internal-barcode", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("barcode").GetString()!;
        var refused = await manager.PostAsync($"/api/v1/products/{factory}/internal-barcode", null);
        var twice = await manager.PostAsync($"/api/v1/products/{first}/internal-barcode", null);

        Assert.True(InternalBarcode.TryGetSequence(one, out var a));
        Assert.True(InternalBarcode.TryGetSequence(two, out var b));
        Assert.Equal(a + 1, b);
        await AssertProblemAsync(refused, HttpStatusCode.UnprocessableEntity, "rule_violation");
        await AssertProblemAsync(twice, HttpStatusCode.UnprocessableEntity, "rule_violation");
        Assert.Equal("6111000000014", await BarcodeOfAsync(manager, factory));
        Assert.Equal(one, await BarcodeOfAsync(manager, first));
    }

    [Fact]
    public async Task Simultaneous_requests_never_produce_the_same_code()
    {
        using var manager = As(Role.Manager);
        var ids = new List<Guid>();
        for (var index = 0; index < 6; index++)
        {
            ids.Add(await CreateProductAsync(manager, $"Article {index}"));
        }

        var answers = await Task.WhenAll(ids.Select(id => manager.PostAsync($"/api/v1/products/{id}/internal-barcode", null)));
        foreach (var (id, answer) in ids.Zip(answers).Where(pair => pair.Second.StatusCode != HttpStatusCode.OK))
        {
            // Une demande qui a perdu la course est rejouée (409) : elle réussit ensuite.
            Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.PostAsync($"/api/v1/products/{id}/internal-barcode", null)).StatusCode);
        }

        var codes = new List<string>();
        foreach (var id in ids)
        {
            codes.Add(await BarcodeOfAsync(manager, id));
        }

        Assert.Equal(6, codes.Distinct().Count());
        Assert.All(codes, code => Assert.True(InternalBarcode.IsValidEan13(code)));
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Other_profiles_get_403(Role role)
    {
        using var manager = As(Role.Manager);
        var id = await CreateProductAsync(manager, "Pain");
        using var other = As(role);

        await AssertProblemAsync(await other.PostAsync($"/api/v1/products/{id}/internal-barcode", null), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_product_of_another_commerce_is_not_found()
    {
        using var manager = As(Role.Manager);
        var id = await CreateProductAsync(manager, "Pain");
        using var stranger = As(Role.Manager, TenantId.New());

        await AssertProblemAsync(await stranger.PostAsync($"/api/v1/products/{id}/internal-barcode", null), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task The_creation_is_in_the_journal_without_changing_anything_else()
    {
        using var owner = As(Role.Owner);
        var id = await CreateProductAsync(owner, "Pain de mie");

        await owner.PostAsync($"/api/v1/products/{id}/internal-barcode", null);

        var journal = await owner.GetStringAsync("/api/v1/audit-log?limit=50");
        Assert.Contains("product.barcode.created", journal, StringComparison.Ordinal);
        Assert.Contains("Code interne créé : Pain de mie", journal, StringComparison.Ordinal);
    }
}
