using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

[Collection(Postgres.Collection)]
public sealed class ApiStockTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private HttpClient As(Role role, TenantId? tenant = null) =>
        _factory.ClientFor(tenant ?? _tenant, UserId.New(), role);

    private static object NewProduct(Guid? id = null, string? barcode = "6111000000011", int threshold = 5) =>
        new
        {
            productId = id ?? Guid.NewGuid(),
            name = "Jus d'orange 1L",
            barcode,
            category = "Boissons",
            purchasePriceFcfa = 400,
            salePriceFcfa = 600,
            alertThreshold = threshold,
            tracksStock = true,
        };

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_manager_builds_the_catalogue_receives_goods_and_reads_the_stock()
    {
        using var manager = As(Role.Manager);
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync("/api/v1/products", NewProduct(id))).StatusCode);

        var reception = await manager.PostAsJsonAsync(
            $"/api/v1/products/{id}/receptions",
            new { movementId = Guid.NewGuid(), quantity = 24, supplier = "Grossiste Koné" });
        Assert.Equal(HttpStatusCode.OK, reception.StatusCode);
        Assert.Equal(24, (await reception.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quantityOnHand").GetInt32());

        var list = await manager.GetFromJsonAsync<JsonElement>("/api/v1/products?search=jus");
        var product = Assert.Single(list.EnumerateArray());
        Assert.Equal(24, product.GetProperty("quantityOnHand").GetInt32());
        Assert.Equal(400, product.GetProperty("purchasePriceFcfa").GetInt64());

        var history = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/products/{id}/movements");
        Assert.Equal("Reception", Assert.Single(history.EnumerateArray()).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_cashier_reads_the_catalogue_without_the_purchase_price_but_cannot_create()
    {
        using var manager = As(Role.Manager);
        await manager.PostAsJsonAsync("/api/v1/products", NewProduct());
        using var cashier = As(Role.Cashier);

        var list = await cashier.GetFromJsonAsync<JsonElement>("/api/v1/products");
        var create = await cashier.PostAsJsonAsync("/api/v1/products", NewProduct(barcode: "999"));

        Assert.Equal(JsonValueKind.Null, Assert.Single(list.EnumerateArray()).GetProperty("purchasePriceFcfa").ValueKind);
        await AssertProblemAsync(create, HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_duplicate_barcode_is_a_409_and_a_replay_is_acknowledged()
    {
        using var manager = As(Role.Manager);
        var id = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/products", NewProduct(id));

        var replay = await manager.PostAsJsonAsync("/api/v1/products", NewProduct(id));
        var duplicate = await manager.PostAsJsonAsync("/api/v1/products", NewProduct());

        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyProcessed").GetBoolean());
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "barcode_taken");
    }

    [Fact]
    public async Task A_loss_without_a_reason_is_refused_and_alerts_list_low_products()
    {
        using var manager = As(Role.Manager);
        var id = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/products", NewProduct(id, threshold: 5));
        await manager.PostAsJsonAsync($"/api/v1/products/{id}/receptions", new { movementId = Guid.NewGuid(), quantity = 6 });

        var refused = await manager.PostAsJsonAsync(
            $"/api/v1/products/{id}/losses", new { movementId = Guid.NewGuid(), quantity = 1, reason = " " });
        var accepted = await manager.PostAsJsonAsync(
            $"/api/v1/products/{id}/losses", new { movementId = Guid.NewGuid(), quantity = 2, reason = "Périmé" });

        await AssertProblemAsync(refused, HttpStatusCode.UnprocessableEntity, "rule_violation");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var alerts = await manager.GetFromJsonAsync<JsonElement>("/api/v1/stock/alerts");
        Assert.Equal(4, Assert.Single(alerts.EnumerateArray()).GetProperty("quantityOnHand").GetInt32());
    }

    [Fact]
    public async Task Another_shop_neither_sees_nor_changes_the_product()
    {
        using var manager = As(Role.Manager);
        var id = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/products", NewProduct(id));
        using var stranger = As(Role.Manager, TenantId.New());

        var list = await stranger.GetFromJsonAsync<JsonElement>("/api/v1/products");
        var reception = await stranger.PostAsJsonAsync(
            $"/api/v1/products/{id}/receptions", new { movementId = Guid.NewGuid(), quantity = 5 });

        Assert.Empty(list.EnumerateArray());
        await AssertProblemAsync(reception, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_recorded_sale_lowers_the_stock_of_the_catalogue_product()
    {
        using var manager = As(Role.Manager);
        var productId = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/products", NewProduct(productId));
        await manager.PostAsJsonAsync(
            $"/api/v1/products/{productId}/receptions", new { movementId = Guid.NewGuid(), quantity = 24 });
        var sessionId = Guid.NewGuid();
        var opened = await manager.PostAsJsonAsync("/api/v1/register-sessions", new
        {
            sessionId,
            pointOfSaleId = Guid.NewGuid(),
            registerId = Guid.NewGuid(),
            openingFloatFcfa = 10_000,
            openedAt = DateTimeOffset.UtcNow,
        });
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

        var sale = await manager.PostAsJsonAsync("/api/v1/sales", new
        {
            saleId = Guid.NewGuid(),
            sessionId,
            localSequence = 1,
            at = DateTimeOffset.UtcNow,
            lines = new[] { new { productId, name = "Jus d'orange 1L", quantity = 3, unitPriceFcfa = 600 } },
            discountFcfa = 0,
            payments = new[] { new { paymentId = Guid.NewGuid(), method = "Cash", amountFcfa = 1_800, tenderedFcfa = 2_000, reference = (string?)null } },
        });

        Assert.Equal(HttpStatusCode.OK, sale.StatusCode);
        var list = await manager.GetFromJsonAsync<JsonElement>("/api/v1/products");
        Assert.Equal(21, Assert.Single(list.EnumerateArray()).GetProperty("quantityOnHand").GetInt32());
    }
}
