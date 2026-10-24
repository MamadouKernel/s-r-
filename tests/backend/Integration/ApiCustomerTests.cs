using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

/// <summary>Clients, crédit et versements de bout en bout (SC-C1 à SC-C10) : API réelle, PostgreSQL réel.</summary>
[Collection(Postgres.Collection)]
public sealed class ApiCustomerTests(PostgresFixture fixture) : IDisposable
{
    private static readonly DateTimeOffset LongAgo = DateTimeOffset.UtcNow.AddDays(-30);

    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<User> SeedAsync(string identifier, Role role, TenantId? tenant = null)
    {
        var owner = tenant ?? _tenant;
        var user = User.Register(UserId.New(), owner, identifier, "Test " + identifier, role, _factory.PinHasher.Hash("1234"), LongAgo);
        await using var db = Db.App(fixture, owner);
        await new UserRepository(db).AddAsync(user, CancellationToken.None);
        await new EfUnitOfWork(db).CommitAsync(CancellationToken.None);
        return user;
    }

    private HttpClient ClientFor(User user, TenantId? tenant = null) => _factory.ClientFor(tenant ?? _tenant, user.Id, user.Role);

    private static string NewPhone() => "07" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<Guid> CreateCustomerAsync(HttpClient client, string name = "Awa Koné", string? phone = null, long? ceiling = null)
    {
        var id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/v1/customers", new { customerId = id, name, phone = phone ?? NewPhone(), creditCeilingFcfa = ceiling });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return id;
    }

    private static async Task<Guid> OpenRegisterAsync(HttpClient cashier)
    {
        var session = Guid.NewGuid();
        var response = await cashier.PostAsJsonAsync("/api/v1/register-sessions", new
        {
            sessionId = session, pointOfSaleId = Guid.NewGuid(), registerId = Guid.NewGuid(), openingFloatFcfa = 0, openedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return session;
    }

    private static object CreditSaleBody(Guid session, Guid customer, long credit, long sequence, Guid? approver = null, Guid? saleId = null) => new
    {
        saleId = saleId ?? Guid.NewGuid(), sessionId = session, localSequence = sequence, at = DateTimeOffset.UtcNow.AddMinutes(-sequence),
        lines = new[] { new { productId = Guid.NewGuid(), name = "Produit", quantity = 1, unitPriceFcfa = credit } },
        discountFcfa = 0, customerId = customer, creditApproverId = approver,
        payments = new[] { new { paymentId = Guid.NewGuid(), method = "Credit", amountFcfa = credit, tenderedFcfa = (long?)null, reference = (string?)null } },
    };

    private static async Task<JsonElement> CustomerAsync(HttpClient client, Guid id)
    {
        var list = await JsonOf(await client.GetAsync("/api/v1/customers?includeInactive=true"));
        return list.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await JsonOf(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_cashier_creates_a_customer_at_the_till_with_the_default_ceiling_and_it_shows_in_the_list_SC_C1()
    {
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var phone = NewPhone();

        var id = await CreateCustomerAsync(cashier, "Awa Koné", phone);

        var customer = await CustomerAsync(cashier, id);
        Assert.Equal("Awa Koné", customer.GetProperty("name").GetString());
        Assert.Equal("+225" + phone, customer.GetProperty("phone").GetString());
        Assert.Equal(20_000, customer.GetProperty("creditCeilingFcfa").GetInt64());
        Assert.Equal(0, customer.GetProperty("debtFcfa").GetInt64());
        Assert.Equal(20_000, customer.GetProperty("availableCreditFcfa").GetInt64());
    }

    [Fact]
    public async Task The_same_phone_in_another_spelling_is_a_conflict_and_a_cashier_cannot_pick_the_ceiling_SC_C1_SC_C10()
    {
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var phone = NewPhone();
        await CreateCustomerAsync(cashier, "Awa Koné", phone);

        var duplicate = await cashier.PostAsJsonAsync("/api/v1/customers", new { customerId = Guid.NewGuid(), name = "Autre", phone = "+225 " + phone });
        var ceiling = await cashier.PostAsJsonAsync("/api/v1/customers", new { customerId = Guid.NewGuid(), name = "Issa Diallo", phone = NewPhone(), creditCeilingFcfa = 999_999 });

        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "phone_taken");
        await AssertProblemAsync(ceiling, HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_credit_sale_adds_to_the_debt_then_the_ceiling_stops_the_next_one_until_a_manager_approves_SC_C2_SC_C3()
    {
        var manager = await SeedAsync("gerant", Role.Manager);
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var customer = await CreateCustomerAsync(cashier);
        var session = await OpenRegisterAsync(cashier);

        var first = await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, customer, 8_000, 1));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(8_000, (await CustomerAsync(cashier, customer)).GetProperty("debtFcfa").GetInt64());

        var refused = await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, customer, 15_000, 2));
        var body = await JsonOf(refused);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("credit_limit_exceeded", body.GetProperty("code").GetString());
        Assert.Contains("il reste 12 000 FCFA", body.GetProperty("detail").GetString()!.Replace(' ', ' '));
        Assert.Equal(8_000, (await CustomerAsync(cashier, customer)).GetProperty("debtFcfa").GetInt64());

        var approved = await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, customer, 15_000, 3, approver: manager.Id.Value));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(23_000, (await CustomerAsync(cashier, customer)).GetProperty("debtFcfa").GetInt64());
    }

    [Fact]
    public async Task A_sale_sent_by_the_sync_is_recorded_even_above_the_ceiling_and_flagged_in_the_journal_SC_C4()
    {
        using var owner = ClientFor(await SeedAsync("proprio", Role.Owner));
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var customer = await CreateCustomerAsync(cashier);
        var session = await OpenRegisterAsync(cashier);
        await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, customer, 15_000, 1));

        var sync = await cashier.PostAsJsonAsync("/api/v1/sales/sync", new { sales = new[] { CreditSaleBody(session, customer, 10_000, 2) } });

        var statuses = (await JsonOf(sync)).GetProperty("statuses");
        Assert.Equal("Accepted", statuses[0].GetProperty("outcome").GetString());
        Assert.Equal(25_000, (await CustomerAsync(cashier, customer)).GetProperty("debtFcfa").GetInt64());
        var journal = await JsonOf(await owner.GetAsync("/api/v1/audit-log?category=credit"));
        var entries = journal.GetProperty("entries").EnumerateArray().ToList();
        var flagged = Assert.Single(entries, entry => entry.GetProperty("action").GetString() == "credit.limit.exceeded");
        Assert.Contains("plafond 20 000 FCFA", flagged.GetProperty("detail").GetString()!.Replace(' ', ' '));
    }

    [Fact]
    public async Task Repayments_lower_the_debt_cannot_exceed_it_and_a_replay_counts_once_SC_C5()
    {
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var customer = await CreateCustomerAsync(cashier);
        var session = await OpenRegisterAsync(cashier);
        await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, customer, 8_000, 1));
        var repayment = Guid.NewGuid();
        var partial = new { repaymentId = repayment, amountFcfa = 5_000, method = "Cash" };

        var first = await cashier.PostAsJsonAsync($"/api/v1/customers/{customer}/repayments", partial);
        var replay = await cashier.PostAsJsonAsync($"/api/v1/customers/{customer}/repayments", partial);
        var over = await cashier.PostAsJsonAsync($"/api/v1/customers/{customer}/repayments", new { repaymentId = Guid.NewGuid(), amountFcfa = 3_001, method = "Cash" });

        Assert.Equal(3_000, (await JsonOf(first)).GetProperty("debtFcfa").GetInt64());
        var replayed = await JsonOf(replay);
        Assert.True(replayed.GetProperty("alreadyProcessed").GetBoolean());
        Assert.Equal(3_000, replayed.GetProperty("debtFcfa").GetInt64());
        await AssertProblemAsync(over, HttpStatusCode.UnprocessableEntity, "rule_violation");
        Assert.Equal(3_000, (await CustomerAsync(cashier, customer)).GetProperty("debtFcfa").GetInt64());
    }

    [Fact]
    public async Task A_mobile_money_reference_serves_once_across_sales_and_repayments_SC_C6()
    {
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var customer = await CreateCustomerAsync(cashier);
        var session = await OpenRegisterAsync(cashier);
        await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, customer, 8_000, 1));
        await cashier.PostAsJsonAsync("/api/v1/sales", new
        {
            saleId = Guid.NewGuid(), sessionId = session, localSequence = 2, at = DateTimeOffset.UtcNow.AddMinutes(-2),
            lines = new[] { new { productId = Guid.NewGuid(), name = "Produit", quantity = 1, unitPriceFcfa = 1_000 } }, discountFcfa = 0,
            payments = new[] { new { paymentId = Guid.NewGuid(), method = "OrangeMoney", amountFcfa = 1_000, tenderedFcfa = (long?)null, reference = (string?)"OM-77" } },
        });

        var usedBySale = await cashier.PostAsJsonAsync($"/api/v1/customers/{customer}/repayments", new { repaymentId = Guid.NewGuid(), amountFcfa = 1_000, method = "OrangeMoney", reference = "om-77" });
        var missing = await cashier.PostAsJsonAsync($"/api/v1/customers/{customer}/repayments", new { repaymentId = Guid.NewGuid(), amountFcfa = 1_000, method = "Wave" });
        var fresh = await cashier.PostAsJsonAsync($"/api/v1/customers/{customer}/repayments", new { repaymentId = Guid.NewGuid(), amountFcfa = 1_000, method = "Wave", reference = "WV-1" });
        var again = await cashier.PostAsJsonAsync($"/api/v1/customers/{customer}/repayments", new { repaymentId = Guid.NewGuid(), amountFcfa = 1_000, method = "Wave", reference = "WV-1" });

        await AssertProblemAsync(usedBySale, HttpStatusCode.Conflict, "mobile_money_reference_used");
        await AssertProblemAsync(missing, HttpStatusCode.UnprocessableEntity, "rule_violation");
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        await AssertProblemAsync(again, HttpStatusCode.Conflict, "mobile_money_reference_used");
        Assert.Equal(7_000, (await CustomerAsync(cashier, customer)).GetProperty("debtFcfa").GetInt64());
    }

    [Fact]
    public async Task Cancelling_a_credit_sale_cancels_its_debt_SC_C7()
    {
        using var manager = ClientFor(await SeedAsync("gerant", Role.Manager));
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var customer = await CreateCustomerAsync(cashier);
        var session = await OpenRegisterAsync(cashier);
        var sale = Guid.NewGuid();
        await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, customer, 8_000, 1, saleId: sale));

        var cancelled = await manager.PostAsJsonAsync($"/api/v1/sales/{sale}/cancel", new { reason = "Erreur de saisie" });

        Assert.True(cancelled.IsSuccessStatusCode);
        Assert.Equal(0, (await CustomerAsync(cashier, customer)).GetProperty("debtFcfa").GetInt64());
    }

    [Fact]
    public async Task Two_simultaneous_credit_sales_for_one_customer_lose_no_debt_SC_C8()
    {
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var customer = await CreateCustomerAsync(cashier, ceiling: null);
        var session = await OpenRegisterAsync(cashier);
        var bodies = new[] { CreditSaleBody(session, customer, 3_000, 1), CreditSaleBody(session, customer, 4_000, 2) };

        var first = await Task.WhenAll(bodies.Select(body => cashier.PostAsJsonAsync("/api/v1/sales", body)));
        // Une écriture concurrente répond 409 « renvoyez » : le client rejoue la même vente, qui s'enregistre alors une seule fois.
        for (var i = 0; i < bodies.Length; i++)
        {
            if (first[i].StatusCode == HttpStatusCode.Conflict)
            {
                Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsJsonAsync("/api/v1/sales", bodies[i])).StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, first[i].StatusCode);
            }
        }

        Assert.Equal(7_000, (await CustomerAsync(cashier, customer)).GetProperty("debtFcfa").GetInt64());
    }

    [Fact]
    public async Task A_customer_with_a_debt_cannot_be_deactivated_and_a_clean_one_disappears_but_its_sales_stay_SC_C9()
    {
        using var manager = ClientFor(await SeedAsync("gerant", Role.Manager));
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var indebted = await CreateCustomerAsync(cashier, "Awa Koné");
        var clean = await CreateCustomerAsync(cashier, "Issa Diallo");
        var session = await OpenRegisterAsync(cashier);
        await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, indebted, 500, 1));

        var refused = await manager.PostAsync($"/api/v1/customers/{indebted}/deactivate", null);
        var done = await manager.PostAsync($"/api/v1/customers/{clean}/deactivate", null);

        await AssertProblemAsync(refused, HttpStatusCode.UnprocessableEntity, "rule_violation");
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var visible = await JsonOf(await cashier.GetAsync("/api/v1/customers"));
        Assert.Equal(["Awa Koné"], visible.EnumerateArray().Select(item => item.GetProperty("name").GetString()));
        var refusedCredit = await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, clean, 100, 2));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refusedCredit.StatusCode);
    }

    [Fact]
    public async Task A_manager_edits_the_ceiling_and_a_cashier_cannot_nor_read_the_repayment_history()
    {
        using var manager = ClientFor(await SeedAsync("gerant", Role.Manager));
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var customer = await CreateCustomerAsync(cashier);

        var edited = await manager.PutAsJsonAsync($"/api/v1/customers/{customer}", new { name = "Awa Koné", phone = (string?)null, creditCeilingFcfa = 50_000 });
        var refused = await cashier.PutAsJsonAsync($"/api/v1/customers/{customer}", new { name = "Awa Koné", phone = (string?)null, creditCeilingFcfa = 90_000 });
        var history = await cashier.GetAsync($"/api/v1/customers/{customer}/repayments");

        Assert.Equal(50_000, (await JsonOf(edited)).GetProperty("creditCeilingFcfa").GetInt64());
        await AssertProblemAsync(refused, HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(history, HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/v1/customers/{customer}/repayments")).StatusCode);
    }

    [Fact]
    public async Task Another_commerce_sees_no_customer_debt_or_repayment_and_the_database_agrees_SC_C10()
    {
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var customer = await CreateCustomerAsync(cashier);
        var session = await OpenRegisterAsync(cashier);
        await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, customer, 500, 1));
        await cashier.PostAsJsonAsync($"/api/v1/customers/{customer}/repayments", new { repaymentId = Guid.NewGuid(), amountFcfa = 100, method = "Cash" });
        var other = TenantId.New();
        using var otherManager = ClientFor(await SeedAsync("autre", Role.Manager, other), other);

        var list = await JsonOf(await otherManager.GetAsync("/api/v1/customers?includeInactive=true"));
        var repay = await otherManager.PostAsJsonAsync($"/api/v1/customers/{customer}/repayments", new { repaymentId = Guid.NewGuid(), amountFcfa = 100, method = "Cash" });
        var history = await otherManager.GetAsync($"/api/v1/customers/{customer}/repayments");

        Assert.Equal(0, list.GetArrayLength());
        await AssertProblemAsync(repay, HttpStatusCode.NotFound, "not_found");
        await AssertProblemAsync(history, HttpStatusCode.NotFound, "not_found");
        await using var asOther = Db.App(fixture, other);
        Assert.Equal(0, await asOther.Database.SqlQuery<long>($"SELECT count(*)::bigint AS \"Value\" FROM customers").SingleAsync());
        Assert.Equal(0, await asOther.Database.SqlQuery<long>($"SELECT count(*)::bigint AS \"Value\" FROM debt_repayments").SingleAsync());
    }

    [Fact]
    public async Task The_database_itself_keeps_repayments_frozen_customers_undeletable_and_debts_non_negative()
    {
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var customer = await CreateCustomerAsync(cashier);
        var session = await OpenRegisterAsync(cashier);
        await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, customer, 500, 1));
        await cashier.PostAsJsonAsync($"/api/v1/customers/{customer}/repayments", new { repaymentId = Guid.NewGuid(), amountFcfa = 100, method = "Cash" });

        await using var db = Db.App(fixture, _tenant);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE debt_repayments SET amount = 1"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM debt_repayments"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM customers"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE customers SET debt = -1"));
    }

    [Fact]
    public async Task The_journal_keeps_repayments_and_credit_decisions_under_one_category()
    {
        using var owner = ClientFor(await SeedAsync("proprio", Role.Owner));
        using var cashier = ClientFor(await SeedAsync("awa", Role.Cashier));
        var customer = await CreateCustomerAsync(cashier);
        var session = await OpenRegisterAsync(cashier);
        await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, customer, 25_000, 1)); // refusé : au-delà du plafond
        await cashier.PostAsJsonAsync("/api/v1/sales", CreditSaleBody(session, customer, 5_000, 2));
        await cashier.PostAsJsonAsync($"/api/v1/customers/{customer}/repayments", new { repaymentId = Guid.NewGuid(), amountFcfa = 1_000, method = "Cash" });

        var journal = await JsonOf(await owner.GetAsync("/api/v1/audit-log?category=credit"));

        var actions = journal.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("action").GetString()).ToList();
        Assert.Contains("credit.limit.refused", actions);
        Assert.Contains("debt.repaid", actions);
    }
}
