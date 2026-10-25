using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Application.Common;
using Soro.Application.Subscription;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Subscription;
using Soro.Domain.Tenancy;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

/// <summary>L'heure réelle : l'abonnement se calcule par rapport à maintenant, comme l'API qui lit le même instant.</summary>
internal sealed class RealClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>L'abonnement après l'essai, de bout en bout : état, paiement déclaré puis validé par l'outil d'exploitation, lecture seule (docs/specs/abonnement).</summary>
[Collection(Postgres.Collection)]
public sealed class ApiSubscriptionTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    /// <summary>Un commerce créé il y a <paramref name="daysAgo"/> jours : son essai de 30 jours se termine en conséquence.</summary>
    private async Task<TenantId> ShopAsync(int daysAgo = 5, string email = "awa@exemple.ci")
    {
        var tenant = TenantId.New();
        await using var db = Db.Owner(fixture);
        await new CommerceRepository(db).AddAsync(
            Commerce.Open(tenant, "Boutique Awa", ActivityType.Shop, EmailAddress.Parse(email), "v1", DateTimeOffset.UtcNow.AddDays(-daysAgo)),
            CancellationToken.None);
        await new EfUnitOfWork(db).CommitAsync(CancellationToken.None);
        return tenant;
    }

    private HttpClient As(TenantId tenant, Role role) => _factory.ClientFor(tenant, UserId.New(), role);

    /// <summary>Une référence tirée au hasard par défaut : la base de test est partagée et une référence ne sert qu'une fois, tous commerces confondus.</summary>
    private static object Payment(Guid? id = null, string? reference = null, long amount = 5_000, string period = "Monthly", string method = "Wave") =>
        new { paymentId = id ?? Guid.NewGuid(), period, method, reference = reference ?? $"W-{Guid.NewGuid():N}"[..16], amountFcfa = amount };

    private async Task<(IPlatformSubscriptions Platform, ValidateSubscriptionPaymentHandler Validate, RefuseSubscriptionPaymentHandler Refuse, SoroDbContext Db)> OpsAsync()
    {
        var db = Db.Owner(fixture);
        var platform = new PlatformSubscriptions(db);
        var clock = new RealClock();
        var audit = new AuditTrail(db);
        var uow = new EfUnitOfWork(db);
        await Task.CompletedTask;
        return (platform, new ValidateSubscriptionPaymentHandler(platform, _factory.Mail, audit, clock, uow), new RefuseSubscriptionPaymentHandler(platform, _factory.Mail, audit, clock, uow), db);
    }

    [Fact]
    public async Task The_owner_reads_the_trial_state_the_prices_and_how_to_pay()
    {
        var tenant = await ShopAsync(daysAgo: 25);
        using var owner = As(tenant, Role.Owner);

        var view = await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscription");

        Assert.Equal("Trial", view.GetProperty("status").GetString());
        Assert.InRange(view.GetProperty("daysLeft").GetInt32(), 4, 5);
        Assert.False(view.GetProperty("isReadOnly").GetBoolean());
        Assert.Equal(5_000, view.GetProperty("monthlyPriceFcfa").GetInt64());
        Assert.Equal(50_000, view.GetProperty("annualPriceFcfa").GetInt64());
        Assert.True(view.GetProperty("payNumbers").TryGetProperty("Wave", out _));
        Assert.Equal(0, view.GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task A_cashier_only_sees_the_state()
    {
        var tenant = await ShopAsync();
        using var cashier = As(tenant, Role.Cashier);

        var view = await cashier.GetFromJsonAsync<JsonElement>("/api/v1/subscription");

        Assert.Equal("Trial", view.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("monthlyPriceFcfa").ValueKind);
        Assert.Equal(JsonValueKind.Null, view.GetProperty("payments").ValueKind);
    }

    [Fact]
    public async Task Declaring_a_payment_is_listed_as_pending_and_replaying_it_changes_nothing()
    {
        var tenant = await ShopAsync();
        using var owner = As(tenant, Role.Owner);
        var id = Guid.NewGuid();

        var first = await owner.PostAsJsonAsync("/api/v1/subscription/payments", Payment(id));
        var replay = await owner.PostAsJsonAsync("/api/v1/subscription/payments", Payment(id));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyProcessed").GetBoolean());
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyProcessed").GetBoolean());
        var view = await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscription");
        var payment = Assert.Single(view.GetProperty("payments").EnumerateArray());
        Assert.Equal("Pending", payment.GetProperty("status").GetString());
        Assert.Equal("Trial", view.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_payment_below_the_price_a_manager_and_a_reused_reference_are_refused()
    {
        var tenant = await ShopAsync();
        var other = await ShopAsync(email: "kone@exemple.ci");
        using var owner = As(tenant, Role.Owner);
        using var otherOwner = As(other, Role.Owner);
        using var manager = As(tenant, Role.Manager);
        var shared = $"W-SH{Guid.NewGuid():N}"[..14];
        await owner.PostAsJsonAsync("/api/v1/subscription/payments", Payment(reference: shared));

        var low = await owner.PostAsJsonAsync("/api/v1/subscription/payments", Payment(amount: 100));
        var byManager = await manager.PostAsJsonAsync("/api/v1/subscription/payments", Payment());
        var reused = await otherOwner.PostAsJsonAsync("/api/v1/subscription/payments", Payment(reference: shared.ToLowerInvariant().Insert(2, " ")));
        var otherMethod = await otherOwner.PostAsJsonAsync("/api/v1/subscription/payments", Payment(reference: shared, method: "OrangeMoney"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, low.StatusCode);
        Assert.Contains("5 000 FCFA", (await low.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, byManager.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal("mobile_money_reference_used", (await reused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, otherMethod.StatusCode);
    }

    [Fact]
    public async Task The_team_validates_a_payment_and_the_shop_becomes_active_and_one_email_confirms()
    {
        var tenant = await ShopAsync(daysAgo: 20, email: "validation@exemple.ci");
        using var owner = As(tenant, Role.Owner);
        var id = Guid.NewGuid();
        await owner.PostAsJsonAsync("/api/v1/subscription/payments", Payment(id));
        var (platform, validate, _, db) = await OpsAsync();
        await using var _ = db;

        var pending = await platform.ListPendingAsync(CancellationToken.None);
        var result = await validate.HandleAsync(new SubscriptionPaymentId(id));

        Assert.Contains(pending, item => item.Payment.Id.Value == id);
        Assert.True(result.IsSuccess, result.Error?.Message);
        var view = await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscription");
        Assert.Equal("Active", view.GetProperty("status").GetString());
        Assert.Equal("Validated", view.GetProperty("payments")[0].GetProperty("status").GetString());
        Assert.InRange(view.GetProperty("daysLeft").GetInt32(), 39, 41); // 10 jours d'essai restants + 1 mois
        Assert.Contains(_factory.Mail.Sent, mail => mail.To.Value == "validation@exemple.ci" && mail.Message.Contains("validé", StringComparison.Ordinal));
        Assert.Empty(await platform.ListPendingAsync(CancellationToken.None) is { } rest ? rest.Where(item => item.Payment.Id.Value == id) : []);
    }

    [Fact]
    public async Task A_refused_payment_shows_its_reason_and_the_owner_can_declare_again()
    {
        var tenant = await ShopAsync(email: "refus@exemple.ci");
        using var owner = As(tenant, Role.Owner);
        var id = Guid.NewGuid();
        await owner.PostAsJsonAsync("/api/v1/subscription/payments", Payment(id, $"W-BAD-{Guid.NewGuid():N}"[..14]));
        var (_, _, refuse, db) = await OpsAsync();
        await using var _ = db;

        var result = await refuse.HandleAsync(new RefuseSubscriptionPaymentCommand(new SubscriptionPaymentId(id), "Référence introuvable sur le relevé."));

        Assert.True(result.IsSuccess);
        var view = await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscription");
        var payment = view.GetProperty("payments")[0];
        Assert.Equal("Refused", payment.GetProperty("status").GetString());
        Assert.Equal("Référence introuvable sur le relevé.", payment.GetProperty("reason").GetString());
        Assert.Equal("Trial", view.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/v1/subscription/payments", Payment())).StatusCode);
    }

    [Fact]
    public async Task Another_shop_sees_nothing_of_a_shops_payments()
    {
        var tenant = await ShopAsync();
        var other = await ShopAsync(email: "autre@exemple.ci");
        using var owner = As(tenant, Role.Owner);
        await owner.PostAsJsonAsync("/api/v1/subscription/payments", Payment());
        using var stranger = As(other, Role.Owner);

        var view = await stranger.GetFromJsonAsync<JsonElement>("/api/v1/subscription");

        Assert.Equal(0, view.GetProperty("payments").GetArrayLength());
    }

    // ABO-07
    [Fact]
    public async Task In_grace_everything_still_works_and_in_read_only_writes_are_refused_but_reading_and_paying_are_not()
    {
        var grace = await ShopAsync(daysAgo: 33);
        var expired = await ShopAsync(daysAgo: 60, email: "expire@exemple.ci");
        using var inGrace = As(grace, Role.Owner);
        using var readOnly = As(expired, Role.Owner);
        var product = new { productId = Guid.NewGuid(), name = "Jus", barcode = (string?)null, category = (string?)null, purchasePriceFcfa = 100, salePriceFcfa = 200, alertThreshold = 0, tracksStock = true };

        var created = await inGrace.PostAsJsonAsync("/api/v1/products", product);
        var refused = await readOnly.PostAsJsonAsync("/api/v1/products", product with { productId = Guid.NewGuid() });
        var read = await readOnly.GetAsync("/api/v1/products");
        var view = await readOnly.GetFromJsonAsync<JsonElement>("/api/v1/subscription");
        var pay = await readOnly.PostAsJsonAsync("/api/v1/subscription/payments", Payment());

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("subscription_expired", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("ReadOnly", view.GetProperty("status").GetString());
        Assert.True(view.GetProperty("isReadOnly").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
    }

    [Fact]
    public async Task A_sync_from_the_offline_queue_is_accepted_in_read_only_and_recorded_in_the_journal()
    {
        var tenant = await ShopAsync(daysAgo: 60);
        using var owner = As(tenant, Role.Owner);

        var sync = await owner.PostAsJsonAsync("/api/v1/sales/sync", new { sales = Array.Empty<object>() });
        var journal = await owner.GetFromJsonAsync<JsonElement>("/api/v1/audit-log?category=subscription");

        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        Assert.Contains(journal.GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("action").GetString() == "subscription.expired.sale");
    }

    [Fact]
    public async Task A_validated_payment_lifts_the_read_only_restriction_at_once()
    {
        var tenant = await ShopAsync(daysAgo: 60, email: "reprise@exemple.ci");
        using var owner = As(tenant, Role.Owner);
        var id = Guid.NewGuid();
        await owner.PostAsJsonAsync("/api/v1/subscription/payments", Payment(id));
        var product = new { productId = Guid.NewGuid(), name = "Jus", barcode = (string?)null, category = (string?)null, purchasePriceFcfa = 100, salePriceFcfa = 200, alertThreshold = 0, tracksStock = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/v1/products", product)).StatusCode);
        var (_, validate, _, db) = await OpsAsync();
        await using var _ = db;

        await validate.HandleAsync(new SubscriptionPaymentId(id));

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/v1/products", product)).StatusCode);
        Assert.Equal("Active", (await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscription")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_shop_that_does_not_exist_yet_is_not_blocked()
    {
        using var client = As(TenantId.New(), Role.Manager);

        var response = await client.PostAsJsonAsync("/api/v1/products", new { productId = Guid.NewGuid(), name = "Jus", purchasePriceFcfa = 1, salePriceFcfa = 2, alertThreshold = 0, tracksStock = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
