using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Infrastructure.Security;

namespace Soro.Integration.Tests;

/// <summary>L'approbation signée d'un gérant, de bout en bout (docs/decisions/0020) : le PIN est tapé une fois, la caisse garde une preuve.</summary>
[Collection(Postgres.Collection)]
public sealed class ApiApprovalTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private HttpClient As(Role role, UserId? id = null) => _factory.ClientFor(_tenant, id ?? UserId.New(), role);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static object Grant(string identifier, string pin, Guid subject, string purpose = "reversal") => new { identifier, pin, subjectId = subject, purpose };

    private async Task RegisterManagerAsync()
    {
        using var owner = As(Role.Owner);
        var created = await owner.PostAsJsonAsync("/api/v1/users", new { userId = Guid.NewGuid(), identifier = "gerant", displayName = "Kofi Diaby", role = "Manager", pin = "482163" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    }

    private static async Task<(Guid Sale, Guid Register)> SellAsync(HttpClient admin, HttpClient client)
    {
        var product = Guid.NewGuid();
        await admin.PostAsJsonAsync("/api/v1/products", new { productId = product, name = "Jus", barcode = (string?)null, category = (string?)null, purchasePriceFcfa = 400, salePriceFcfa = 1_000, alertThreshold = 0, tracksStock = true });
        await admin.PostAsJsonAsync($"/api/v1/products/{product}/receptions", new { movementId = Guid.NewGuid(), quantity = 10, supplier = (string?)null, purchasePriceFcfa = (long?)400 });
        var session = Guid.NewGuid();
        var register = Guid.NewGuid();
        await client.PostAsJsonAsync("/api/v1/register-sessions", new { sessionId = session, pointOfSaleId = Guid.NewGuid(), registerId = register, openingFloatFcfa = 10_000, openedAt = DateTimeOffset.UtcNow.AddHours(-2) });
        var sale = Guid.NewGuid();
        var recorded = await client.PostAsJsonAsync("/api/v1/sales", new
        {
            saleId = sale, sessionId = session, localSequence = 1, at = DateTimeOffset.UtcNow.AddMinutes(-30),
            lines = new[] { new { productId = product, name = "Jus", quantity = 2, unitPriceFcfa = 1_000 } },
            discountFcfa = 0,
            payments = new[] { new { paymentId = Guid.NewGuid(), method = "Cash", amountFcfa = 2_000, tenderedFcfa = (long?)2_000, reference = (string?)null } },
        });
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        return (sale, register);
    }

    [Fact]
    public async Task A_cashier_gets_an_approval_with_the_identifier_and_pin_of_a_manager_then_cancels_with_it()
    {
        await RegisterManagerAsync();
        using var till = As(Role.Cashier);
        var (sale, register) = await SellAsync(As(Role.Manager), till);

        var granted = await till.PostAsJsonAsync("/api/v1/approvals", Grant("GERANT", "482163", sale));
        var body = await JsonOf(granted);
        var cancelled = await till.PostAsJsonAsync($"/api/v1/sales/{sale}/cancel", new { reason = "Erreur de saisie", registerId = register, approvalToken = body.GetProperty("token").GetString() });

        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal("Kofi Diaby", body.GetProperty("approverName").GetString());
        Assert.True(cancelled.StatusCode == HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        Assert.Equal(2_000, (await JsonOf(cancelled)).GetProperty("totalFcfa").GetInt64());
    }

    [Fact]
    public async Task A_wrong_pin_or_unknown_identifier_is_a_401_with_the_same_message_and_a_token_is_useless_for_another_sale()
    {
        await RegisterManagerAsync();
        using var till = As(Role.Cashier);
        var (sale, _) = await SellAsync(As(Role.Manager), till);

        var wrong = await till.PostAsJsonAsync("/api/v1/approvals", Grant("gerant", "000000", sale));
        var unknown = await till.PostAsJsonAsync("/api/v1/approvals", Grant("fantome", "482163", sale));
        var token = (await JsonOf(await till.PostAsJsonAsync("/api/v1/approvals", Grant("gerant", "482163", Guid.NewGuid())))).GetProperty("token").GetString();
        var misused = await till.PostAsJsonAsync($"/api/v1/sales/{sale}/cancel", new { reason = "Erreur", approvalToken = token });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal((await JsonOf(wrong)).GetProperty("detail").GetString(), (await JsonOf(unknown)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, misused.StatusCode);
    }

    [Fact]
    public async Task A_large_discount_goes_through_with_an_approval_token_and_not_without()
    {
        await RegisterManagerAsync();
        using var till = As(Role.Cashier);
        using var manager = As(Role.Manager);
        var product = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/products", new { productId = product, name = "Jus", barcode = (string?)null, category = (string?)null, purchasePriceFcfa = 400, salePriceFcfa = 1_000, alertThreshold = 0, tracksStock = false });
        var session = Guid.NewGuid();
        await till.PostAsJsonAsync("/api/v1/register-sessions", new { sessionId = session, pointOfSaleId = Guid.NewGuid(), registerId = Guid.NewGuid(), openingFloatFcfa = 0, openedAt = DateTimeOffset.UtcNow.AddHours(-1) });
        object Sale(Guid id, string? token) => new
        {
            saleId = id, sessionId = session, localSequence = id == Guid.Empty ? 0 : 1 + (token is null ? 0 : 1), at = DateTimeOffset.UtcNow.AddMinutes(-5),
            lines = new[] { new { productId = product, name = "Jus", quantity = 1, unitPriceFcfa = 1_000 } },
            discountFcfa = 300,
            discountApprovalToken = token,
            payments = new[] { new { paymentId = Guid.NewGuid(), method = "Cash", amountFcfa = 700, tenderedFcfa = (long?)700, reference = (string?)null } },
        };
        var sale = Guid.NewGuid();

        var refused = await till.PostAsJsonAsync("/api/v1/sales", Sale(Guid.NewGuid(), null));
        var granted = await till.PostAsJsonAsync("/api/v1/approvals", Grant("gerant", "482163", sale, "discount"));
        var accepted = await till.PostAsJsonAsync("/api/v1/sales", Sale(sale, (await JsonOf(granted)).GetProperty("token").GetString()));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public void The_seal_only_opens_for_its_shop_sale_purpose_and_time_and_refuses_a_tampered_token()
    {
        var seal = new HmacApprovalSeal(new byte[32]);
        var tenant = TenantId.New();
        var approver = UserId.New();
        var subject = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var token = seal.Issue(tenant, approver, subject, "reversal", now.AddMinutes(15));

        Assert.Equal(approver, seal.Open(token, tenant, subject, "reversal", now));
        Assert.Null(seal.Open(token, TenantId.New(), subject, "reversal", now));
        Assert.Null(seal.Open(token, tenant, Guid.NewGuid(), "reversal", now));
        Assert.Null(seal.Open(token, tenant, subject, "discount", now));
        Assert.Null(seal.Open(token, tenant, subject, "reversal", now.AddMinutes(16)));
        Assert.Null(seal.Open(token[..^2] + "AA", tenant, subject, "reversal", now));
        Assert.Null(seal.Open(token.Replace('.', '!'), tenant, subject, "reversal", now));
        Assert.Null(seal.Open(string.Empty, tenant, subject, "reversal", now));
        Assert.Null(new HmacApprovalSeal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 })
            .Open(token, tenant, subject, "reversal", now));
    }
}
