using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

[Collection(Postgres.Collection)]
public sealed class ApiIdentityTests(PostgresFixture fixture) : IDisposable
{
    private static readonly DateTimeOffset LongAgo = DateTimeOffset.UtcNow.AddDays(-30);

    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    /// <summary>Crée un utilisateur directement en base (le premier propriétaire n'a pas d'autre moyen d'exister).</summary>
    private async Task<User> SeedAsync(string identifier, Role role, string pin = "1234", TenantId? tenant = null)
    {
        var owner = tenant ?? _tenant;
        var user = User.Register(UserId.New(), owner, identifier, "Test " + identifier, role, _factory.PinHasher.Hash(pin), LongAgo);
        await using var db = Db.App(fixture, owner);
        await new UserRepository(db).AddAsync(user, CancellationToken.None);
        await new EfUnitOfWork(db).CommitAsync(CancellationToken.None);
        return user;
    }

    private async Task<HttpResponseMessage> LoginAsync(HttpClient client, string identifier, string pin, TenantId? tenant = null) =>
        await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { tenantId = (tenant ?? _tenant).Value, identifier, pin });

    private async Task<HttpClient> LoggedInAsync(string identifier, string pin = "1234")
    {
        var client = _factory.CreateClient();
        var response = await LoginAsync(client, identifier, pin);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static object RegisterBody(string identifier, string role, string pin = "4321", Guid? id = null) =>
        new { userId = id ?? Guid.NewGuid(), identifier, displayName = "Nouvel employé", role, pin };

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Logging_in_returns_a_token_that_carries_the_commerce_user_and_role()
    {
        var owner = await SeedAsync("proprio", Role.Owner);
        using var client = _factory.CreateClient();

        var response = await LoginAsync(client, "Proprio", "1234");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.Equal("Owner", body.GetProperty("user").GetProperty("role").GetString());
        var token = new JsonWebTokenHandler().ReadJsonWebToken(body.GetProperty("accessToken").GetString());
        Assert.Equal(owner.Id.Value.ToString(), token.GetClaim("sub").Value);
        Assert.Equal(_tenant.Value.ToString(), token.GetClaim("tenant_id").Value);
        Assert.Equal("Owner", token.GetClaim("role").Value);
        Assert.True(token.ValidTo > DateTime.UtcNow.AddHours(7) && token.ValidTo < DateTime.UtcNow.AddHours(9));
    }

    [Theory]
    [InlineData("proprio", "9999")]
    [InlineData("inconnu", "1234")]
    [InlineData("proprio", "12ab")]
    public async Task A_wrong_login_gets_the_same_unauthorized_answer(string identifier, string pin)
    {
        await SeedAsync("proprio", Role.Owner);
        using var client = _factory.CreateClient();

        var response = await LoginAsync(client, identifier, pin);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "invalid_credentials");
    }

    [Fact]
    public async Task A_user_of_another_commerce_cannot_log_in_here()
    {
        await SeedAsync("proprio", Role.Owner, tenant: TenantId.New());
        using var client = _factory.CreateClient();

        var response = await LoginAsync(client, "proprio", "1234");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "invalid_credentials");
    }

    [Fact]
    public async Task Five_wrong_pins_lock_the_account()
    {
        await SeedAsync("proprio", Role.Owner);
        using var client = _factory.CreateClient();
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            await LoginAsync(client, "proprio", "0000");
        }

        var response = await LoginAsync(client, "proprio", "1234");

        await AssertProblemAsync(response, HttpStatusCode.Locked, "account_locked");
    }

    [Fact]
    public async Task A_malformed_login_body_is_a_bad_request()
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_owner_registers_a_cashier_who_can_then_log_in_and_open_a_register()
    {
        await SeedAsync("proprio", Role.Owner);
        using var owner = await LoggedInAsync("proprio");

        var registration = await owner.PostAsJsonAsync("/api/v1/users", RegisterBody("caissiere", "Cashier"));
        using var cashier = await LoggedInAsync("caissiere", "4321");
        var opening = await cashier.PostAsJsonAsync("/api/v1/register-sessions", new
        {
            sessionId = Guid.NewGuid(),
            pointOfSaleId = Guid.NewGuid(),
            registerId = Guid.NewGuid(),
            openingFloatFcfa = 10_000,
            openedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });

        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        Assert.Equal(HttpStatusCode.OK, opening.StatusCode);
    }

    [Fact]
    public async Task A_cashier_cannot_register_users()
    {
        await SeedAsync("caissiere", Role.Cashier);
        using var cashier = await LoggedInAsync("caissiere");

        var response = await cashier.PostAsJsonAsync("/api/v1/users", RegisterBody("autre", "Cashier"));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task An_identifier_already_used_is_a_conflict_and_a_weak_pin_is_unprocessable()
    {
        await SeedAsync("proprio", Role.Owner);
        await SeedAsync("caissiere", Role.Cashier);
        using var owner = await LoggedInAsync("proprio");

        var taken = await owner.PostAsJsonAsync("/api/v1/users", RegisterBody("caissiere", "Cashier"));
        var weak = await owner.PostAsJsonAsync("/api/v1/users", RegisterBody("nouvelle", "Cashier", pin: "12"));

        await AssertProblemAsync(taken, HttpStatusCode.Conflict, "identifier_taken");
        await AssertProblemAsync(weak, HttpStatusCode.UnprocessableEntity, "rule_violation");
    }

    [Fact]
    public async Task A_disabled_user_can_no_longer_log_in()
    {
        await SeedAsync("proprio", Role.Owner);
        var cashier = await SeedAsync("caissiere", Role.Cashier);
        using var owner = await LoggedInAsync("proprio");

        var disabled = await owner.PostAsync($"/api/v1/users/{cashier.Id.Value}/disable", content: null);
        using var anonymous = _factory.CreateClient();
        var login = await LoginAsync(anonymous, "caissiere", "1234");

        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        await AssertProblemAsync(login, HttpStatusCode.Unauthorized, "invalid_credentials");
    }

    [Fact]
    public async Task A_discount_can_be_approved_by_a_manager_known_to_the_directory()
    {
        var manager = await SeedAsync("gerant", Role.Manager);
        await SeedAsync("caissiere", Role.Cashier);
        using var cashier = await LoggedInAsync("caissiere");
        var sessionId = Guid.NewGuid();
        await cashier.PostAsJsonAsync("/api/v1/register-sessions", new
        {
            sessionId,
            pointOfSaleId = Guid.NewGuid(),
            registerId = Guid.NewGuid(),
            openingFloatFcfa = 10_000,
            openedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });

        var response = await cashier.PostAsJsonAsync("/api/v1/sales", new
        {
            saleId = Guid.NewGuid(),
            sessionId,
            localSequence = 1,
            at = DateTimeOffset.UtcNow.AddHours(-1),
            lines = new[] { new { productId = Guid.NewGuid(), name = "Produit", quantity = 1, unitPriceFcfa = 1_000 } },
            discountFcfa = 200,
            payments = new[]
            {
                new { paymentId = Guid.NewGuid(), method = "Cash", amountFcfa = 800, tenderedFcfa = (long?)800, reference = (string?)null },
            },
            discountApproverId = manager.Id.Value,
            discountApproverPin = "1234",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_manager_identifier_without_the_pin_does_not_approve_a_discount()
    {
        var manager = await SeedAsync("gerant", Role.Manager);
        await SeedAsync("caissiere", Role.Cashier);
        using var cashier = await LoggedInAsync("caissiere");
        var sessionId = Guid.NewGuid();
        await cashier.PostAsJsonAsync("/api/v1/register-sessions", new
        {
            sessionId,
            pointOfSaleId = Guid.NewGuid(),
            registerId = Guid.NewGuid(),
            openingFloatFcfa = 10_000,
            openedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });

        object Sale(string? pin) => new
        {
            saleId = Guid.NewGuid(),
            sessionId,
            localSequence = 1,
            at = DateTimeOffset.UtcNow.AddHours(-1),
            lines = new[] { new { productId = Guid.NewGuid(), name = "Produit", quantity = 1, unitPriceFcfa = 1_000 } },
            discountFcfa = 200,
            payments = new[] { new { paymentId = Guid.NewGuid(), method = "Cash", amountFcfa = 800, tenderedFcfa = (long?)800, reference = (string?)null } },
            discountApproverId = manager.Id.Value,
            discountApproverPin = pin,
        };

        var withoutPin = await cashier.PostAsJsonAsync("/api/v1/sales", Sale(null));
        var wrongPin = await cashier.PostAsJsonAsync("/api/v1/sales", Sale("0000"));

        Assert.Equal(HttpStatusCode.Forbidden, withoutPin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongPin.StatusCode);
    }

    [Fact]
    public async Task Login_attempts_are_rate_limited()
    {
        using var limited = new ApiFactory(fixture, loginPermitsPerMinute: 3);
        using var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { tenantId = _tenant.Value, identifier = "inconnu", pin = "0000" });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }
}
