using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Integration.Tests;

[Collection(Postgres.Collection)]
public sealed class ApiTests(PostgresFixture fixture) : IDisposable
{
    // Relatif à l'horloge : une annulation, datée par le serveur, ne peut pas précéder la vente.
    private static readonly DateTimeOffset Morning = DateTimeOffset.UtcNow.AddHours(-3);

    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();
    private readonly UserId _cashier = UserId.New();

    public void Dispose() => _factory.Dispose();

    private HttpClient Cashier() => _factory.ClientFor(_tenant, _cashier, Role.Cashier);

    private static object OpenBody(Guid sessionId, Guid? registerId = null, long openingFloat = 10_000) =>
        new
        {
            sessionId,
            pointOfSaleId = Guid.NewGuid(),
            registerId = registerId ?? Guid.NewGuid(),
            openingFloatFcfa = openingFloat,
            openedAt = Morning,
        };

    private static object CashSaleBody(Guid sessionId, long price = 1_000, long sequence = 1, long? tendered = null, Guid? saleId = null) =>
        new
        {
            saleId = saleId ?? Guid.NewGuid(),
            sessionId,
            localSequence = sequence,
            at = Morning.AddMinutes(10),
            lines = new[] { new { productId = Guid.NewGuid(), name = "Produit", quantity = 1, unitPriceFcfa = price } },
            discountFcfa = 0,
            payments = new[]
            {
                new { paymentId = Guid.NewGuid(), method = "Cash", amountFcfa = price, tenderedFcfa = tendered ?? price, reference = (string?)null },
            },
        };

    private static object MobileSaleBody(Guid sessionId, string reference, long sequence) =>
        new
        {
            saleId = Guid.NewGuid(),
            sessionId,
            localSequence = sequence,
            at = Morning.AddMinutes(10),
            lines = new[] { new { productId = Guid.NewGuid(), name = "Produit", quantity = 1, unitPriceFcfa = 1_000 } },
            discountFcfa = 0,
            payments = new[]
            {
                new { paymentId = Guid.NewGuid(), method = "OrangeMoney", amountFcfa = 1_000, tenderedFcfa = (long?)null, reference },
            },
        };

    private async Task<Guid> OpenSessionAsync(HttpClient client)
    {
        var sessionId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/v1/register-sessions", OpenBody(sessionId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return sessionId;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Requests_without_a_token_are_unauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/register-sessions", OpenBody(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task A_token_missing_the_tenant_or_the_user_is_rejected(bool hasTenant, bool hasUser)
    {
        using var client = _factory.CreateClient();
        var token = _factory.Token(
            hasTenant ? _tenant.Value.ToString() : null,
            hasUser ? _cashier.Value.ToString() : null,
            Role.Cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/register-sessions", OpenBody(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Opens_a_session_and_acknowledges_a_replay()
    {
        using var client = Cashier();
        var body = OpenBody(Guid.NewGuid());

        var first = await JsonAsync(await client.PostAsJsonAsync("/api/v1/register-sessions", body));
        var replay = await JsonAsync(await client.PostAsJsonAsync("/api/v1/register-sessions", body));

        Assert.False(first.GetProperty("alreadyProcessed").GetBoolean());
        Assert.True(replay.GetProperty("alreadyProcessed").GetBoolean());
    }

    [Fact]
    public async Task Records_a_cash_sale_and_returns_the_change()
    {
        using var client = Cashier();
        var sessionId = await OpenSessionAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/sales", CashSaleBody(sessionId, price: 2_500, tendered: 3_000));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sale = await JsonAsync(response);
        Assert.Equal(2_500, sale.GetProperty("netTotalFcfa").GetInt64());
        Assert.Equal(500, sale.GetProperty("changeFcfa").GetInt64());
    }

    [Fact]
    public async Task Another_tenant_gets_a_not_found_problem()
    {
        using var owner = Cashier();
        var sessionId = await OpenSessionAsync(owner);
        using var intruder = _factory.ClientFor(TenantId.New(), UserId.New(), Role.Cashier);

        var response = await intruder.PostAsJsonAsync("/api/v1/sales", CashSaleBody(sessionId));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_reused_mobile_money_reference_is_a_conflict()
    {
        using var client = Cashier();
        var sessionId = await OpenSessionAsync(client);
        await client.PostAsJsonAsync("/api/v1/sales", MobileSaleBody(sessionId, "MP-API-1", 1));

        var response = await client.PostAsJsonAsync("/api/v1/sales", MobileSaleBody(sessionId, "mp-api-1", 2));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "mobile_money_reference_used");
    }

    [Fact]
    public async Task A_business_rule_violation_is_unprocessable()
    {
        using var client = Cashier();
        var sessionId = await OpenSessionAsync(client);
        var body = new
        {
            saleId = Guid.NewGuid(),
            sessionId,
            localSequence = 1,
            at = Morning.AddMinutes(10),
            lines = new[] { new { productId = Guid.NewGuid(), name = "Produit", quantity = 1, unitPriceFcfa = 1_000 } },
            discountFcfa = 0,
            payments = new[]
            {
                new { paymentId = Guid.NewGuid(), method = "Cash", amountFcfa = 900, tenderedFcfa = (long?)900, reference = (string?)null },
            },
        };

        var response = await client.PostAsJsonAsync("/api/v1/sales", body);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "rule_violation");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"saleId":"00000000-0000-0000-0000-000000000001","payments":[{"method":"Bitcoin"}]}""")]
    public async Task A_malformed_body_is_a_bad_request(string json)
    {
        using var client = Cashier();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/sales", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_a_manager_can_cancel_a_sale()
    {
        using var cashier = Cashier();
        var sessionId = await OpenSessionAsync(cashier);
        var saleId = Guid.NewGuid();
        await cashier.PostAsJsonAsync("/api/v1/sales", CashSaleBody(sessionId, saleId: saleId));
        using var manager = _factory.ClientFor(_tenant, UserId.New(), Role.Manager);
        var cancellation = new { reason = "Erreur de saisie" };

        var refused = await cashier.PostAsJsonAsync($"/api/v1/sales/{saleId}/cancel", cancellation);
        var accepted = await manager.PostAsJsonAsync($"/api/v1/sales/{saleId}/cancel", cancellation);

        await AssertProblemAsync(refused, HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task Closes_the_session_and_reports_the_variance_to_the_manager_but_not_to_the_cashier()
    {
        using var client = Cashier();
        var sessionId = await OpenSessionAsync(client);
        await client.PostAsJsonAsync("/api/v1/sales", CashSaleBody(sessionId, price: 2_000));

        var first = await client.PostAsJsonAsync(
            $"/api/v1/register-sessions/{sessionId}/close",
            new { countedCashFcfa = 11_500, at = Morning.AddHours(8) });
        var closing = await JsonAsync(first);

        // 500 FCFA de manque : dans la tolérance, la caisse est close, et le caissier ne voit ni l'attendu ni l'écart.
        Assert.True(closing.GetProperty("closed").GetBoolean());
        Assert.Equal(11_500, closing.GetProperty("countedCashFcfa").GetInt64());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, closing.GetProperty("expectedCashFcfa").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, closing.GetProperty("varianceFcfa").ValueKind);

        using var manager = _factory.ClientFor(_tenant, UserId.New(), Role.Manager);
        var replay = await JsonAsync(await manager.PostAsJsonAsync(
            $"/api/v1/register-sessions/{sessionId}/close",
            new { countedCashFcfa = 11_500, at = Morning.AddHours(8) }));
        Assert.Equal(12_000, replay.GetProperty("expectedCashFcfa").GetInt64());
        Assert.Equal(-500, replay.GetProperty("varianceFcfa").GetInt64());
    }

    [Fact]
    public async Task A_sync_batch_reports_each_sale_and_can_be_replayed()
    {
        using var client = Cashier();
        var sessionId = await OpenSessionAsync(client);
        var batch = new { sales = new[] { CashSaleBody(sessionId, sequence: 2), CashSaleBody(sessionId, sequence: 1) } };

        var first = await JsonAsync(await client.PostAsJsonAsync("/api/v1/sales/sync", batch));
        var replay = await JsonAsync(await client.PostAsJsonAsync("/api/v1/sales/sync", batch));

        Assert.All(first.GetProperty("statuses").EnumerateArray(), s => Assert.Equal("Accepted", s.GetProperty("outcome").GetString()));
        Assert.All(replay.GetProperty("statuses").EnumerateArray(), s => Assert.Equal("AlreadyProcessed", s.GetProperty("outcome").GetString()));
    }

    [Fact]
    public async Task A_sale_that_reuses_a_local_number_is_rejected_alone_and_never_blocks_the_batch()
    {
        using var client = Cashier();
        var sessionId = await OpenSessionAsync(client);
        var batch = new { sales = new[] { CashSaleBody(sessionId, sequence: 1), CashSaleBody(sessionId, sequence: 1), CashSaleBody(sessionId, sequence: 2) } };

        var response = await client.PostAsJsonAsync("/api/v1/sales/sync", batch);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var statuses = (await JsonAsync(response)).GetProperty("statuses").EnumerateArray().Select(s => s.GetProperty("outcome").GetString()!).ToArray();
        Assert.Equal(["Accepted", "Rejected", "Accepted"], statuses);
    }

    [Fact]
    public async Task Keeps_serving_requests_after_many_calls()
    {
        using var client = Cashier();
        var sessionId = await OpenSessionAsync(client);

        for (var sequence = 1; sequence <= 30; sequence++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/sales", CashSaleBody(sessionId, sequence: sequence));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Requests_beyond_the_rate_limit_are_rejected()
    {
        using var limited = new ApiFactory(fixture, permitPerMinute: 3);
        using var client = limited.ClientFor(_tenant, _cashier, Role.Cashier);
        var body = OpenBody(Guid.NewGuid());

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/v1/register-sessions", body)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.Equal(HttpStatusCode.OK, statuses[0]);
    }
}
