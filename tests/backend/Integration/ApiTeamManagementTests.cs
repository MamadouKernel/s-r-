using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Tenancy;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

/// <summary>Gestion complète de l'équipe de bout en bout (docs/specs/equipe-gestion-complete) : API réelle, PostgreSQL réel.</summary>
[Collection(Postgres.Collection)]
public sealed class ApiTeamManagementTests(PostgresFixture fixture) : IDisposable
{
    private static readonly DateTimeOffset LongAgo = DateTimeOffset.UtcNow.AddDays(-30);

    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Vérifie le statut et le code d'un problème, et rend son corps pour d'autres vérifications.</summary>
    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await JsonOf(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        return body;
    }

    private async Task<User> SeedUserAsync(string identifier, Role role, TenantId? tenant = null)
    {
        var commerce = tenant ?? _tenant;
        var user = User.Register(UserId.New(), commerce, identifier, "Test " + identifier, role, _factory.PinHasher.Hash("1234"), LongAgo);
        await using var db = Db.App(fixture, commerce);
        await new UserRepository(db).AddAsync(user, CancellationToken.None);
        await new EfUnitOfWork(db).CommitAsync(CancellationToken.None);
        return user;
    }

    private async Task<User> SeedOwnerAsync()
    {
        var owner = User.RegisterOwner(
            UserId.New(), _tenant, EmailAddress.Parse($"o{Guid.NewGuid():N}@exemple.ci"), "Mamadou", "password-hash", LongAgo);
        await using var db = Db.App(fixture, _tenant);
        await new UserRepository(db).AddAsync(owner, CancellationToken.None);
        await new EfUnitOfWork(db).CommitAsync(CancellationToken.None);
        return owner;
    }

    private HttpClient ClientFor(User user, DateTimeOffset? issuedAt = null) =>
        _factory.ClientFor(user.Tenant, user.Id, user.Role, issuedAt ?? DateTimeOffset.UtcNow);

    private async Task<HttpResponseMessage> LoginAsync(string identifier, string pin)
    {
        using var anonymous = _factory.CreateClient();
        return await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { tenantId = _tenant.Value, identifier, pin });
    }

    private static string Url(User user, string action) => $"/api/v1/users/{user.Id.Value}/{action}";

    [Fact]
    public async Task The_owner_gives_a_cashier_a_new_pin_which_replaces_the_old_one()
    {
        var owner = await SeedOwnerAsync();
        var cashier = await SeedUserAsync("awa", Role.Cashier);
        using var client = ClientFor(owner);

        var response = await client.PostAsJsonAsync(Url(cashier, "pin"), new { pin = "987654" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync("awa", "987654")).StatusCode);
        await AssertProblemAsync(await LoginAsync("awa", "1234"), HttpStatusCode.Unauthorized, "invalid_credentials");
    }

    [Fact]
    public async Task A_locked_cashier_can_log_in_again_after_a_new_pin()
    {
        var owner = await SeedOwnerAsync();
        await SeedUserAsync("awa", Role.Cashier);
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            await LoginAsync("awa", "0000");
        }

        await AssertProblemAsync(await LoginAsync("awa", "1234"), HttpStatusCode.Locked, "account_locked");
        using var client = ClientFor(owner);
        var team = await JsonOf(await client.GetAsync("/api/v1/users"));
        var id = team.GetProperty("users").EnumerateArray().Single(user => user.GetProperty("identifier").GetString() == "awa").GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync($"/api/v1/users/{id}/pin", new { pin = "5555" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync("awa", "5555")).StatusCode);
    }

    [Fact]
    public async Task A_new_pin_ends_the_open_session_at_once_but_not_the_next_one()
    {
        var owner = await SeedOwnerAsync();
        var cashier = await SeedUserAsync("awa", Role.Cashier);
        using var oldSession = ClientFor(cashier, DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(HttpStatusCode.OK, (await oldSession.GetAsync("/api/v1/products")).StatusCode);
        using var ownerClient = ClientFor(owner);

        await ownerClient.PostAsJsonAsync(Url(cashier, "pin"), new { pin = "5555" });

        var cut = await AssertProblemAsync(await oldSession.GetAsync("/api/v1/products"), HttpStatusCode.Unauthorized, "session_outdated");
        Assert.Contains("reconnectez-vous", cut.GetProperty("detail").GetString(), StringComparison.Ordinal);
        using var fresh = ClientFor(cashier, DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/v1/products")).StatusCode);
    }

    [Fact]
    public async Task A_disabled_member_loses_the_session_already_open()
    {
        var owner = await SeedOwnerAsync();
        var cashier = await SeedUserAsync("awa", Role.Cashier);
        using var session = ClientFor(cashier, DateTimeOffset.UtcNow.AddSeconds(-1));
        using var ownerClient = ClientFor(owner);

        await ownerClient.PostAsync(Url(cashier, "disable"), null);

        await AssertProblemAsync(await session.GetAsync("/api/v1/products"), HttpStatusCode.Unauthorized, "session_outdated");
    }

    [Fact]
    public async Task The_owner_is_not_cut_off_by_changes_made_to_others()
    {
        var owner = await SeedOwnerAsync();
        var cashier = await SeedUserAsync("awa", Role.Cashier);
        using var ownerClient = ClientFor(owner, DateTimeOffset.UtcNow.AddHours(-3));

        await ownerClient.PostAsJsonAsync(Url(cashier, "pin"), new { pin = "5555" });

        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task A_disabled_member_comes_back_with_a_new_pin_only()
    {
        var owner = await SeedOwnerAsync();
        var cashier = await SeedUserAsync("awa", Role.Cashier);
        using var client = ClientFor(owner);
        await client.PostAsync(Url(cashier, "disable"), null);

        var response = await client.PostAsJsonAsync(Url(cashier, "enable"), new { pin = "6666" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertProblemAsync(await LoginAsync("awa", "1234"), HttpStatusCode.Unauthorized, "invalid_credentials");
        var login = await LoginAsync("awa", "6666");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var session = _factory.CreateClient();
        session.DefaultRequestHeaders.Authorization = new("Bearer", (await JsonOf(login)).GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/v1/products")).StatusCode);
    }

    [Fact]
    public async Task Reactivating_an_active_member_and_a_bad_pin_are_refused_clearly()
    {
        var owner = await SeedOwnerAsync();
        var cashier = await SeedUserAsync("awa", Role.Cashier);
        using var client = ClientFor(owner);

        var active = await client.PostAsJsonAsync(Url(cashier, "enable"), new { pin = "6666" });
        await client.PostAsync(Url(cashier, "disable"), null);
        var badPin = await client.PostAsJsonAsync(Url(cashier, "enable"), new { pin = "12" });
        var resetWhileDisabled = await client.PostAsJsonAsync(Url(cashier, "pin"), new { pin = "6666" });

        await AssertProblemAsync(active, HttpStatusCode.UnprocessableEntity, "rule_violation");
        await AssertProblemAsync(badPin, HttpStatusCode.UnprocessableEntity, "rule_violation");
        await AssertProblemAsync(resetWhileDisabled, HttpStatusCode.UnprocessableEntity, "rule_violation");
    }

    [Fact]
    public async Task The_owner_changes_a_role_and_the_next_login_carries_the_new_one()
    {
        var owner = await SeedOwnerAsync();
        var cashier = await SeedUserAsync("awa", Role.Cashier);
        using var client = ClientFor(owner);

        var response = await client.PutAsJsonAsync(Url(cashier, "role"), new { role = "Manager" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var login = await JsonOf(await LoginAsync("awa", "1234"));
        Assert.Equal("Manager", login.GetProperty("user").GetProperty("role").GetString());
        var team = await JsonOf(await client.GetAsync("/api/v1/users"));
        Assert.Equal(
            "Manager",
            team.GetProperty("users").EnumerateArray().Single(user => user.GetProperty("identifier").GetString() == "awa").GetProperty("role").GetString());
    }

    [Fact]
    public async Task A_role_change_ends_the_open_session_so_the_old_rights_do_not_linger()
    {
        var owner = await SeedOwnerAsync();
        var manager = await SeedUserAsync("gerant", Role.Manager);
        using var session = ClientFor(manager, DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/v1/users")).StatusCode);
        using var client = ClientFor(owner);

        await client.PutAsJsonAsync(Url(manager, "role"), new { role = "Cashier" });

        await AssertProblemAsync(await session.GetAsync("/api/v1/users"), HttpStatusCode.Unauthorized, "session_outdated");
    }

    [Fact]
    public async Task The_role_change_is_in_the_journal_in_plain_french()
    {
        var owner = await SeedOwnerAsync();
        var cashier = await SeedUserAsync("awa", Role.Cashier);
        using var client = ClientFor(owner);

        await client.PutAsJsonAsync(Url(cashier, "role"), new { role = "Manager" });
        await client.PostAsJsonAsync(Url(cashier, "pin"), new { pin = "987654" });

        var journal = await client.GetStringAsync("/api/v1/audit-log?limit=50");
        Assert.Contains("Test awa : Caissier → Gérant", journal, StringComparison.Ordinal);
        Assert.Contains("user.pin.reset", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("987654", journal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rights_are_enforced_by_the_server()
    {
        var owner = await SeedOwnerAsync();
        var manager = await SeedUserAsync("gerant", Role.Manager);
        var otherManager = await SeedUserAsync("gerant2", Role.Manager);
        var cashier = await SeedUserAsync("awa", Role.Cashier);
        using var managerClient = ClientFor(manager);
        using var cashierClient = ClientFor(cashier);

        await AssertProblemAsync(
            await managerClient.PostAsJsonAsync(Url(otherManager, "pin"), new { pin = "5555" }), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(
            await managerClient.PutAsJsonAsync(Url(cashier, "role"), new { role = "Manager" }), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(
            await managerClient.PostAsJsonAsync(Url(manager, "pin"), new { pin = "5555" }), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(
            await managerClient.PostAsJsonAsync(Url(owner, "pin"), new { pin = "5555" }), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(
            await cashierClient.PostAsJsonAsync(Url(cashier, "pin"), new { pin = "5555" }), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(
            await cashierClient.PutAsJsonAsync(Url(manager, "role"), new { role = "Cashier" }), HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(HttpStatusCode.NoContent, (await managerClient.PostAsJsonAsync(Url(cashier, "pin"), new { pin = "5555" })).StatusCode);
    }

    [Fact]
    public async Task A_member_of_another_commerce_is_not_found()
    {
        var owner = await SeedOwnerAsync();
        var foreignTenant = TenantId.New();
        var foreign = await SeedUserAsync("etranger", Role.Cashier, foreignTenant);
        using var client = ClientFor(owner);

        await AssertProblemAsync(await client.PostAsJsonAsync(Url(foreign, "pin"), new { pin = "5555" }), HttpStatusCode.NotFound, "not_found");
        await AssertProblemAsync(await client.PostAsJsonAsync(Url(foreign, "enable"), new { pin = "5555" }), HttpStatusCode.NotFound, "not_found");
        await AssertProblemAsync(await client.PutAsJsonAsync(Url(foreign, "role"), new { role = "Manager" }), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task The_team_list_says_which_actions_each_member_allows()
    {
        var owner = await SeedOwnerAsync();
        await SeedUserAsync("awa", Role.Cashier);
        using var client = ClientFor(owner);

        var team = await JsonOf(await client.GetAsync("/api/v1/users"));

        var awa = team.GetProperty("users").EnumerateArray().Single(user => user.GetProperty("identifier").GetString() == "awa");
        Assert.True(awa.GetProperty("canRenewCode").GetBoolean());
        Assert.True(awa.GetProperty("canChangeRole").GetBoolean());
        Assert.False(awa.GetProperty("canEnable").GetBoolean());
    }

    // ---- Liens d'accès en attente ---------------------------------------------------------------------------

    private async Task SeedCommerceAsync()
    {
        await using var db = Db.App(fixture, _tenant);
        await new CommerceRepository(db).AddAsync(
            Commerce.Open(_tenant, "Supérette du Carrefour", ActivityType.Shop, EmailAddress.Parse($"c{Guid.NewGuid():N}@exemple.ci"), "2026-10-brouillon", DateTimeOffset.UtcNow),
            CancellationToken.None);
        await new EfUnitOfWork(db).CommitAsync(CancellationToken.None);
    }

    private static async Task<(Guid Id, string Token)> InviteAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/devices/invitations", new { suggestedName = name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        return (body.GetProperty("invitationId").GetGuid(), body.GetProperty("token").GetString()!);
    }

    private async Task<HttpResponseMessage> EnrollAsync(string token)
    {
        using var anonymous = _factory.CreateClient();
        return await anonymous.PostAsJsonAsync("/api/v1/devices/enroll", new { token });
    }

    [Fact]
    public async Task Pending_links_are_listed_without_any_secret_and_a_revoked_one_disappears()
    {
        await SeedCommerceAsync();
        var manager = await SeedUserAsync("gerant", Role.Manager);
        using var client = ClientFor(manager);
        var first = await InviteAsync(client, "Caisse 1");
        await InviteAsync(client, "Caisse 2");

        var list = await client.GetAsync("/api/v1/devices/invitations");
        var raw = await list.Content.ReadAsStringAsync();
        var revoke = await client.PostAsync($"/api/v1/devices/invitations/{first.Id}/revoke", null);
        var after = await JsonOf(await client.GetAsync("/api/v1/devices/invitations"));

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain(first.Token, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Test gerant", raw, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(["Caisse 2"], after.EnumerateArray().Select(item => item.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_revoked_link_answers_like_an_expired_one()
    {
        await SeedCommerceAsync();
        var manager = await SeedUserAsync("gerant", Role.Manager);
        using var client = ClientFor(manager);
        var link = await InviteAsync(client, "Caisse 1");

        await client.PostAsync($"/api/v1/devices/invitations/{link.Id}/revoke", null);
        var answer = await EnrollAsync(link.Token);

        var body = await AssertProblemAsync(answer, HttpStatusCode.UnprocessableEntity, "invalid_invitation");
        Assert.Contains("a expiré", body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoking_twice_is_harmless_and_a_used_link_points_to_the_device_list()
    {
        await SeedCommerceAsync();
        var manager = await SeedUserAsync("gerant", Role.Manager);
        using var client = ClientFor(manager);
        var cancelled = await InviteAsync(client, "Caisse 1");
        var used = await InviteAsync(client, "Caisse 2");
        Assert.Equal(HttpStatusCode.OK, (await EnrollAsync(used.Token)).StatusCode);

        var first = await client.PostAsync($"/api/v1/devices/invitations/{cancelled.Id}/revoke", null);
        var second = await client.PostAsync($"/api/v1/devices/invitations/{cancelled.Id}/revoke", null);
        var tooLate = await client.PostAsync($"/api/v1/devices/invitations/{used.Id}/revoke", null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var body = await AssertProblemAsync(tooLate, HttpStatusCode.UnprocessableEntity, "rule_violation");
        Assert.Contains("liste des appareils", body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashiers_cannot_list_or_revoke_links_and_other_commerces_are_invisible()
    {
        await SeedCommerceAsync();
        var manager = await SeedUserAsync("gerant", Role.Manager);
        var cashier = await SeedUserAsync("awa", Role.Cashier);
        using var managerClient = ClientFor(manager);
        var link = await InviteAsync(managerClient, "Caisse 1");
        var foreignTenant = TenantId.New();
        var foreignManager = await SeedUserAsync("etranger", Role.Manager, foreignTenant);
        using var foreignClient = _factory.ClientFor(foreignTenant, foreignManager.Id, Role.Manager, DateTimeOffset.UtcNow);
        using var cashierClient = ClientFor(cashier);

        await AssertProblemAsync(await cashierClient.GetAsync("/api/v1/devices/invitations"), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(
            await cashierClient.PostAsync($"/api/v1/devices/invitations/{link.Id}/revoke", null), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(
            await foreignClient.PostAsync($"/api/v1/devices/invitations/{link.Id}/revoke", null), HttpStatusCode.NotFound, "not_found");
        Assert.Empty((await JsonOf(await foreignClient.GetAsync("/api/v1/devices/invitations"))).EnumerateArray());
    }
}
