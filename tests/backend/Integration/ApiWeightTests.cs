using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Integration.Tests;

/// <summary>Vente au poids de bout en bout (docs/specs/vente-au-poids, lot 1) : API réelle, PostgreSQL réel.</summary>
[Collection(Postgres.Collection)]
public sealed class ApiWeightTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private HttpClient Manager() => _factory.ClientFor(_tenant, UserId.New(), Role.Manager);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreateRiceAsync(HttpClient client, string unit = "Kilogram")
    {
        var id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            "/api/v1/products",
            new { productId = id, name = "Riz parfumé", barcode = (string?)null, category = "Épicerie", purchasePriceFcfa = 600, salePriceFcfa = 800, alertThreshold = 0, tracksStock = true, unit });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = await client.PostAsJsonAsync($"/api/v1/products/{id}/receptions", new { movementId = Guid.NewGuid(), quantity = 25_000, supplier = (string?)null, purchasePriceFcfa = (long?)600 });
        Assert.Equal(HttpStatusCode.OK, received.StatusCode);
        return id;
    }

    private static async Task<Guid> OpenSessionAsync(HttpClient client)
    {
        var sessionId = Guid.NewGuid();
        var opened = await client.PostAsJsonAsync("/api/v1/register-sessions", new
        {
            sessionId, pointOfSaleId = Guid.NewGuid(), registerId = Guid.NewGuid(), openingFloatFcfa = 10_000, openedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        return sessionId;
    }

    private static object Sale(Guid session, Guid product, int quantity, long total, string? unit, long price = 800, long sequence = 1) => new
    {
        saleId = Guid.NewGuid(), sessionId = session, localSequence = sequence, at = DateTimeOffset.UtcNow.AddMinutes(-30 + sequence),
        lines = new[] { new { productId = product, name = "Riz parfumé", quantity, unitPriceFcfa = price, unit } },
        discountFcfa = 0,
        payments = new[] { new { paymentId = Guid.NewGuid(), method = "Cash", amountFcfa = total, tenderedFcfa = (long?)total, reference = (string?)null } },
    };

    private static async Task<JsonElement> ProductAsync(HttpClient client, Guid id) =>
        (await JsonOf(await client.GetAsync("/api/v1/products"))).EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);

    [Fact]
    public async Task A_product_is_created_by_the_kilogram_and_the_catalogue_says_so()
    {
        using var manager = Manager();
        var id = await CreateRiceAsync(manager);

        var rice = await ProductAsync(manager, id);

        Assert.Equal("Kilogram", rice.GetProperty("unit").GetString());
        Assert.Equal(25_000, rice.GetProperty("quantityOnHand").GetInt32());
    }

    [Fact]
    public async Task One_kilo_two_fifty_at_800_francs_costs_1000_francs_and_leaves_23_750_grams()
    {
        using var manager = Manager();
        var rice = await CreateRiceAsync(manager);
        var session = await OpenSessionAsync(manager);

        var response = await manager.PostAsJsonAsync("/api/v1/sales", Sale(session, rice, 1_250, 1_000, "Kilogram"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1_000, (await JsonOf(response)).GetProperty("netTotalFcfa").GetInt64());
        Assert.Equal(23_750, (await ProductAsync(manager, rice)).GetProperty("quantityOnHand").GetInt32());
    }

    [Fact]
    public async Task A_payment_that_ignores_the_unit_does_not_cover_the_sale()
    {
        using var manager = Manager();
        var rice = await CreateRiceAsync(manager);
        var session = await OpenSessionAsync(manager);

        var response = await manager.PostAsJsonAsync("/api/v1/sales", Sale(session, rice, 1_250, 1_250, "Kilogram"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(25_000, (await ProductAsync(manager, rice)).GetProperty("quantityOnHand").GetInt32());
    }

    [Fact]
    public async Task An_old_device_that_sends_no_unit_is_told_to_update_and_nothing_is_recorded()
    {
        using var manager = Manager();
        var rice = await CreateRiceAsync(manager);
        var session = await OpenSessionAsync(manager);

        var response = await manager.PostAsJsonAsync("/api/v1/sales", Sale(session, rice, 2, 1_600, unit: null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("rule_violation", body.GetProperty("code").GetString());
        Assert.Contains("mettre à jour", body.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(25_000, (await ProductAsync(manager, rice)).GetProperty("quantityOnHand").GetInt32());
    }

    [Fact]
    public async Task The_sale_keeps_its_unit_and_its_rounded_total_in_the_history()
    {
        using var manager = Manager();
        var rice = await CreateRiceAsync(manager);
        var session = await OpenSessionAsync(manager);
        var sale = Sale(session, rice, 333, 266, "Kilogram"); // 266,4 → 266

        var recorded = await manager.PostAsJsonAsync("/api/v1/sales", sale);
        var saleId = (await JsonOf(recorded)).GetProperty("saleId").GetGuid();
        var detail = await JsonOf(await manager.GetAsync($"/api/v1/sales/{saleId}"));

        var line = detail.GetProperty("lines")[0];
        Assert.Equal("Kilogram", line.GetProperty("unit").GetString());
        Assert.Equal(333, line.GetProperty("quantity").GetInt32());
        Assert.Equal(266, line.GetProperty("totalFcfa").GetInt64());
    }

    [Fact]
    public async Task The_unit_of_a_product_with_a_history_cannot_change()
    {
        using var manager = Manager();
        var rice = await CreateRiceAsync(manager); // une réception : il a déjà un historique, même à stock nul
        var update = new { name = "Riz parfumé", barcode = (string?)null, category = "Épicerie", purchasePriceFcfa = 600, salePriceFcfa = 800, alertThreshold = 0, unit = "Piece" };

        var response = await manager.PutAsJsonAsync($"/api/v1/products/{rice}", update);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("historique", (await JsonOf(response)).GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("Kilogram", (await ProductAsync(manager, rice)).GetProperty("unit").GetString());
    }

    [Fact]
    public async Task The_ranking_counts_the_revenue_of_weighed_lines_rounded_line_by_line_and_names_the_unit()
    {
        using var manager = Manager();
        var rice = await CreateRiceAsync(manager);
        var session = await OpenSessionAsync(manager);
        await manager.PostAsJsonAsync("/api/v1/sales", Sale(session, rice, 333, 266, "Kilogram", sequence: 1));
        await manager.PostAsJsonAsync("/api/v1/sales", Sale(session, rice, 333, 266, "Kilogram", sequence: 2));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("o"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("o"));
        var ranking = await JsonOf(await manager.GetAsync($"/api/v1/reports/top-products?from={from}&to={to}&by=revenue&limit=5"));

        var item = ranking.EnumerateArray().Single();
        Assert.Equal(532, item.GetProperty("revenueFcfa").GetInt64()); // 2 × 266, pas 2 × 266,4
        Assert.Equal(666, item.GetProperty("quantity").GetInt32());
        Assert.Equal("Kilogram", item.GetProperty("unit").GetString());
    }
}
