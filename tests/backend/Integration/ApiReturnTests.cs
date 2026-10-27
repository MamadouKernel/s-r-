using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Integration.Tests;

/// <summary>Annulations et retours de bout en bout (docs/specs/annulations-remises-retours, lot 1) : API réelle, PostgreSQL réel.</summary>
[Collection(Postgres.Collection)]
public sealed class ApiReturnTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private HttpClient Manager() => _factory.ClientFor(_tenant, UserId.New(), Role.Manager);

    private HttpClient Cashier(UserId? id = null) => _factory.ClientFor(_tenant, id ?? UserId.New(), Role.Cashier);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreateJuiceAsync(HttpClient client)
    {
        var id = Guid.NewGuid();
        var created = await client.PostAsJsonAsync(
            "/api/v1/products",
            new { productId = id, name = "Jus", barcode = (string?)null, category = (string?)null, purchasePriceFcfa = 400, salePriceFcfa = 1_000, alertThreshold = 0, tracksStock = true });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var received = await client.PostAsJsonAsync($"/api/v1/products/{id}/receptions", new { movementId = Guid.NewGuid(), quantity = 10, supplier = (string?)null, purchasePriceFcfa = (long?)400 });
        Assert.Equal(HttpStatusCode.OK, received.StatusCode);
        return id;
    }

    private static async Task<(Guid Session, Guid Register)> OpenSessionAsync(HttpClient client)
    {
        var session = Guid.NewGuid();
        var register = Guid.NewGuid();
        var opened = await client.PostAsJsonAsync("/api/v1/register-sessions", new
        {
            sessionId = session, pointOfSaleId = Guid.NewGuid(), registerId = register, openingFloatFcfa = 10_000, openedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        return (session, register);
    }

    private static async Task<Guid> SellTwoAsync(HttpClient client, Guid session, Guid product)
    {
        var id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/v1/sales", new
        {
            saleId = id, sessionId = session, localSequence = 1, at = DateTimeOffset.UtcNow.AddMinutes(-30),
            lines = new[] { new { productId = product, name = "Jus", quantity = 2, unitPriceFcfa = 1_000 } },
            discountFcfa = 0,
            payments = new[] { new { paymentId = Guid.NewGuid(), method = "Cash", amountFcfa = 2_000, tenderedFcfa = (long?)2_000, reference = (string?)null } },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return id;
    }

    private static object ReturnBody(Guid? operation = null, int quantity = 1, Guid? register = null, Guid? approver = null, string? pin = null, bool resellable = true) => new
    {
        operationId = operation ?? Guid.NewGuid(),
        reason = "Client mécontent",
        lines = new[] { new { lineIndex = 0, quantity, resellable } },
        registerId = register,
        approverId = approver,
        approverPin = pin,
    };

    private static async Task<int> StockAsync(HttpClient client, Guid product) =>
        (await JsonOf(await client.GetAsync("/api/v1/products"))).EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == product).GetProperty("quantityOnHand").GetInt32();

    [Fact]
    public async Task A_manager_returns_one_article_the_stock_comes_back_and_the_drawer_gives_the_cash()
    {
        using var manager = Manager();
        var product = await CreateJuiceAsync(manager);
        var (session, _) = await OpenSessionAsync(manager);
        var sale = await SellTwoAsync(manager, session, product);

        var response = await manager.PostAsJsonAsync($"/api/v1/sales/{sale}/returns", ReturnBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var done = await JsonOf(response);
        Assert.Equal(1_000, done.GetProperty("totalFcfa").GetInt64());
        Assert.Equal(1_000, done.GetProperty("cashFcfa").GetInt64());
        Assert.Equal(9, await StockAsync(manager, product));
        var sessions = await JsonOf(await manager.GetAsync("/api/v1/register-sessions"));
        var summary = sessions.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == session);
        Assert.Equal(11_000, summary.GetProperty("expectedCashFcfa").GetInt64());
        Assert.Equal(1_000, summary.GetProperty("cashRefundedFcfa").GetInt64());
    }

    [Fact]
    public async Task The_sale_detail_lists_its_returns_so_the_screen_knows_what_is_left_to_return()
    {
        using var manager = Manager();
        var product = await CreateJuiceAsync(manager);
        var (session, _) = await OpenSessionAsync(manager);
        var sale = await SellTwoAsync(manager, session, product);
        await manager.PostAsJsonAsync($"/api/v1/sales/{sale}/returns", ReturnBody());

        var detail = await JsonOf(await manager.GetAsync($"/api/v1/sales/{sale}"));

        var item = Assert.Single(detail.GetProperty("returns").EnumerateArray());
        Assert.Equal(1_000, item.GetProperty("totalFcfa").GetInt64());
        Assert.Equal(1, Assert.Single(item.GetProperty("lines").EnumerateArray()).GetProperty("quantity").GetInt32());
        var list = await JsonOf(await manager.GetAsync("/api/v1/sales"));
        Assert.Equal(1_000, list.EnumerateArray().Single().GetProperty("returnedFcfa").GetInt64());
    }

    [Fact]
    public async Task The_same_return_sent_twice_refunds_once_and_a_return_beyond_what_was_sold_is_refused()
    {
        using var manager = Manager();
        var product = await CreateJuiceAsync(manager);
        var (session, _) = await OpenSessionAsync(manager);
        var sale = await SellTwoAsync(manager, session, product);
        var operation = Guid.NewGuid();

        var first = await manager.PostAsJsonAsync($"/api/v1/sales/{sale}/returns", ReturnBody(operation));
        var again = await manager.PostAsJsonAsync($"/api/v1/sales/{sale}/returns", ReturnBody(operation));
        var tooMuch = await manager.PostAsJsonAsync($"/api/v1/sales/{sale}/returns", ReturnBody(quantity: 2));

        Assert.False((await JsonOf(first)).GetProperty("alreadyProcessed").GetBoolean());
        Assert.True((await JsonOf(again)).GetProperty("alreadyProcessed").GetBoolean());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMuch.StatusCode);
        Assert.Equal(9, await StockAsync(manager, product));
    }

    [Fact]
    public async Task Two_returns_of_the_last_article_at_once_cannot_both_succeed()
    {
        using var manager = Manager();
        var product = await CreateJuiceAsync(manager);
        var (session, _) = await OpenSessionAsync(manager);
        var sale = await SellTwoAsync(manager, session, product);
        await manager.PostAsJsonAsync($"/api/v1/sales/{sale}/returns", ReturnBody());

        var answers = await Task.WhenAll(
            manager.PostAsJsonAsync($"/api/v1/sales/{sale}/returns", ReturnBody()),
            manager.PostAsJsonAsync($"/api/v1/sales/{sale}/returns", ReturnBody()));

        Assert.Equal(1, answers.Count(answer => answer.StatusCode == HttpStatusCode.OK));
        Assert.Equal(10, await StockAsync(manager, product));
    }

    [Fact]
    public async Task A_cashier_needs_a_manager_to_validate_with_his_pin()
    {
        using var manager = Manager();
        var product = await CreateJuiceAsync(manager);
        var (session, _) = await OpenSessionAsync(manager);
        var sale = await SellTwoAsync(manager, session, product);
        var cashierId = UserId.New();
        using var cashier = Cashier(cashierId);

        var refused = await cashier.PostAsJsonAsync($"/api/v1/sales/{sale}/returns", ReturnBody());

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(8, await StockAsync(manager, product));
    }

    [Fact]
    public async Task A_cancellation_puts_everything_back_marks_the_sale_cancelled_and_refuses_a_second_cancellation()
    {
        using var manager = Manager();
        var product = await CreateJuiceAsync(manager);
        var (session, _) = await OpenSessionAsync(manager);
        var sale = await SellTwoAsync(manager, session, product);

        var cancelled = await manager.PostAsJsonAsync($"/api/v1/sales/{sale}/cancel", new { reason = "Erreur de saisie" });
        var second = await manager.PostAsJsonAsync($"/api/v1/sales/{sale}/cancel", new { reason = "Encore" });

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(2_000, (await JsonOf(cancelled)).GetProperty("totalFcfa").GetInt64());
        Assert.Equal(10, await StockAsync(manager, product));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var detail = await JsonOf(await manager.GetAsync($"/api/v1/sales/{sale}"));
        Assert.Equal("Cancelled", detail.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Another_shop_cannot_see_or_return_the_sale()
    {
        using var manager = Manager();
        var product = await CreateJuiceAsync(manager);
        var (session, _) = await OpenSessionAsync(manager);
        var sale = await SellTwoAsync(manager, session, product);
        using var stranger = _factory.ClientFor(TenantId.New(), UserId.New(), Role.Manager);

        var response = await stranger.PostAsJsonAsync($"/api/v1/sales/{sale}/returns", ReturnBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_lower_the_net_sales_of_the_activity_report_and_the_best_sellers()
    {
        using var manager = Manager();
        var product = await CreateJuiceAsync(manager);
        var (session, _) = await OpenSessionAsync(manager);
        var sale = await SellTwoAsync(manager, session, product);
        await manager.PostAsJsonAsync($"/api/v1/sales/{sale}/returns", ReturnBody());
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));

        var activity = await JsonOf(await manager.GetAsync($"/api/v1/reports/activity?from={from}&to={to}"));
        var ranking = await JsonOf(await manager.GetAsync($"/api/v1/reports/top-products?from={from}&to={to}&by=revenue"));

        Assert.Equal(1_000, activity.GetProperty("netTotalFcfa").GetInt64());
        Assert.Equal(1, activity.GetProperty("returnsCount").GetInt32());
        Assert.Equal(1_000, activity.GetProperty("returnsTotalFcfa").GetInt64());
        var top = Assert.Single(ranking.EnumerateArray());
        Assert.Equal(1_000, top.GetProperty("revenueFcfa").GetInt64());
        Assert.Equal(1, top.GetProperty("quantity").GetInt32());
    }
}
