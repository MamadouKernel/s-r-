using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Integration.Tests;

[Collection(Postgres.Collection)]
public sealed class ApiOfflineLoginTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Offline_logins_sent_with_the_queue_appear_in_the_security_journal_once()
    {
        var cashier = UserId.New();
        using var client = _factory.ClientFor(_tenant, cashier, Role.Cashier);
        using var owner = _factory.ClientFor(_tenant, UserId.New(), Role.Owner);
        var body = new { logins = new[] { new { eventId = Guid.NewGuid(), userId = cashier.Value, startedAt = DateTimeOffset.UtcNow.AddHours(-6), endedAt = (DateTimeOffset?)DateTimeOffset.UtcNow.AddHours(-1) } } };

        var first = await client.PostAsJsonAsync("/api/v1/offline-logins", body);
        var replay = await client.PostAsJsonAsync("/api/v1/offline-logins", body);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recorded").GetInt32());
        Assert.Equal(0, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recorded").GetInt32());
        var journal = await owner.GetFromJsonAsync<JsonElement>("/api/v1/audit-log?category=security");
        Assert.Single(journal.GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("action").GetString() == "login.offline");
    }
}
