using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Integration.Tests;

/// <summary>Sorties d'espèces, clôture à l'aveugle et rapport du jour de bout en bout (docs/specs/cloture-sorties-rapport, lot 1).</summary>
[Collection(Postgres.Collection)]
public sealed class ApiCashTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private HttpClient As(Role role, UserId? id = null) => _factory.ClientFor(_tenant, id ?? UserId.New(), role);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> OpenAsync(HttpClient client, long openingFloat = 10_000)
    {
        var session = Guid.NewGuid();
        var opened = await client.PostAsJsonAsync("/api/v1/register-sessions", new
        {
            sessionId = session, pointOfSaleId = Guid.NewGuid(), registerId = Guid.NewGuid(), openingFloatFcfa = openingFloat, openedAt = DateTimeOffset.UtcNow.AddHours(-3),
        });
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        return session;
    }

    private static object Movement(Guid? id = null, string kind = "Expense", long amount = 1_500, string? reason = "Transport", string? token = null) =>
        new { movementId = id ?? Guid.NewGuid(), kind, amountFcfa = amount, reason, at = DateTimeOffset.UtcNow.AddHours(-1), approvalToken = token };

    private async Task RegisterManagerAsync()
    {
        using var owner = As(Role.Owner);
        var created = await owner.PostAsJsonAsync("/api/v1/users", new { userId = Guid.NewGuid(), identifier = "gerant", displayName = "Kofi Diaby", role = "Manager", pin = "482163" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    }

    [Fact]
    public async Task A_cashier_records_an_expense_and_replaying_it_changes_nothing()
    {
        var cashier = UserId.New();
        using var till = As(Role.Cashier, cashier);
        var session = await OpenAsync(till);
        var id = Guid.NewGuid();

        var first = await till.PostAsJsonAsync($"/api/v1/register-sessions/{session}/movements", Movement(id));
        var replay = await till.PostAsJsonAsync($"/api/v1/register-sessions/{session}/movements", Movement(id));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False((await JsonOf(first)).GetProperty("alreadyProcessed").GetBoolean());
        Assert.True((await JsonOf(replay)).GetProperty("alreadyProcessed").GetBoolean());
        using var manager = As(Role.Manager);
        var sessions = await JsonOf(await manager.GetAsync("/api/v1/register-sessions"));
        var mine = sessions.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == session);
        Assert.Equal(8_500, mine.GetProperty("expectedCashFcfa").GetInt64());
        Assert.Equal(1_500, mine.GetProperty("cashPaidOutFcfa").GetInt64());
    }

    [Fact]
    public async Task A_big_expense_needs_a_manager_approval_given_for_that_movement_and_that_amount()
    {
        await RegisterManagerAsync();
        using var till = As(Role.Cashier);
        var session = await OpenAsync(till, 50_000);
        var id = Guid.NewGuid();

        var refused = await till.PostAsJsonAsync($"/api/v1/register-sessions/{session}/movements", Movement(id, amount: 8_000));
        var granted = await till.PostAsJsonAsync("/api/v1/approvals", new { identifier = "gerant", pin = "482163", subjectId = id, purpose = "cash-out", amount = 8_000 });
        var token = (await JsonOf(granted)).GetProperty("token").GetString();
        var accepted = await till.PostAsJsonAsync($"/api/v1/register-sessions/{session}/movements", Movement(id, amount: 8_000, token: token));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task A_cashier_closes_blind_is_asked_to_recount_and_never_receives_the_expected_amount()
    {
        using var till = As(Role.Cashier);
        var session = await OpenAsync(till);

        var first = await JsonOf(await till.PostAsJsonAsync($"/api/v1/register-sessions/{session}/close", new { countedCashFcfa = 12_000, at = DateTimeOffset.UtcNow }));
        var second = await till.PostAsJsonAsync($"/api/v1/register-sessions/{session}/close", new { countedCashFcfa = 8_000, at = DateTimeOffset.UtcNow, recount = true });
        var final = await JsonOf(second);

        Assert.True(first.GetProperty("needsRecount").GetBoolean());
        Assert.False(first.GetProperty("closed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("expectedCashFcfa").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("varianceFcfa").ValueKind);
        Assert.True(final.GetProperty("closed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, final.GetProperty("expectedCashFcfa").ValueKind);
        Assert.Equal(JsonValueKind.Null, final.GetProperty("varianceFcfa").ValueKind);

        using var owner = As(Role.Owner);
        var sessions = await JsonOf(await owner.GetAsync("/api/v1/register-sessions"));
        var closed = sessions.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == session);
        Assert.Equal(-2_000, closed.GetProperty("varianceFcfa").GetInt64());
        var journal = await JsonOf(await owner.GetAsync("/api/v1/audit-log?category=cash"));
        Assert.Contains(journal.GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("action").GetString() == "session.variance");
    }

    [Fact]
    public async Task A_manager_who_closes_sees_the_expected_amount_and_an_expense_is_not_a_shortage()
    {
        using var manager = As(Role.Manager);
        var session = await OpenAsync(manager);
        await manager.PostAsJsonAsync($"/api/v1/register-sessions/{session}/movements", Movement(amount: 1_500));

        var result = await JsonOf(await manager.PostAsJsonAsync($"/api/v1/register-sessions/{session}/close", new { countedCashFcfa = 8_500, at = DateTimeOffset.UtcNow, denominations = new Dictionary<int, int> { [5_000] = 1, [1_000] = 3, [500] = 1 } }));

        Assert.Equal(8_500, result.GetProperty("expectedCashFcfa").GetInt64());
        Assert.Equal(0, result.GetProperty("varianceFcfa").GetInt64());
    }

    [Fact]
    public async Task The_day_report_gathers_the_sales_the_outflows_the_sessions_and_is_closed_to_cashiers()
    {
        using var till = As(Role.Cashier);
        var session = await OpenAsync(till, 50_000);
        await till.PostAsJsonAsync($"/api/v1/register-sessions/{session}/movements", Movement(amount: 2_000, reason: "Sachets et emballages"));
        await till.PostAsJsonAsync($"/api/v1/register-sessions/{session}/movements", Movement(kind: "Topup", amount: 5_000, reason: null));
        using var owner = As(Role.Owner);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var report = await JsonOf(await owner.GetAsync($"/api/v1/reports/day?date={today:yyyy-MM-dd}"));
        var forbidden = await till.GetAsync($"/api/v1/reports/day?date={today:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(2, report.GetProperty("movements").GetArrayLength());
        Assert.Contains(report.GetProperty("movements").EnumerateArray(), item => item.GetProperty("reason").GetString() == "Sachets et emballages" && item.GetProperty("amountFcfa").GetInt64() == 2_000);
        var open = report.GetProperty("sessions").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == session);
        Assert.Equal(53_000, open.GetProperty("expectedCashFcfa").GetInt64());
        Assert.Equal("Open", open.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Another_shop_cannot_see_or_record_on_this_session()
    {
        using var till = As(Role.Cashier);
        var session = await OpenAsync(till);
        using var stranger = _factory.ClientFor(TenantId.New(), UserId.New(), Role.Manager);

        var response = await stranger.PostAsJsonAsync($"/api/v1/register-sessions/{session}/movements", Movement());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

[Collection(Postgres.Collection)]
public sealed class EveningReportLogTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_day_is_marked_sent_once_per_shop_and_other_days_or_shops_stay_free()
    {
        var shop = TenantId.New();
        var day = new DateOnly(2026, 10, 15);
        await using (var db = Db.Owner(fixture))
        {
            var log = new Soro.Infrastructure.Persistence.EveningReportLog(db);
            Assert.False(await log.WasSentAsync(shop, day, CancellationToken.None));
            await log.RecordSentAsync(shop, day, DateTimeOffset.UtcNow, CancellationToken.None);
            await new Soro.Infrastructure.Persistence.EfUnitOfWork(db).CommitAsync(CancellationToken.None);
        }

        await using var again = Db.Owner(fixture);
        var reread = new Soro.Infrastructure.Persistence.EveningReportLog(again);
        Assert.True(await reread.WasSentAsync(shop, day, CancellationToken.None));
        Assert.False(await reread.WasSentAsync(shop, day.AddDays(1), CancellationToken.None));
        Assert.False(await reread.WasSentAsync(TenantId.New(), day, CancellationToken.None));
    }
}
