using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Tenancy;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

/// <summary>Enrôlement et révocation des appareils de bout en bout (SC-A1 à SC-A8) : API réelle, PostgreSQL réel.</summary>
[Collection(Postgres.Collection)]
public sealed class ApiDeviceTests(PostgresFixture fixture) : IDisposable
{
    private static readonly DateTimeOffset LongAgo = DateTimeOffset.UtcNow.AddDays(-30);

    private readonly ApiFactory _factory = new(fixture, devicesRequired: true);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await JsonOf(response)).GetProperty("code").GetString());
    }

    private async Task<User> SeedUserAsync(string identifier, Role role, TenantId? tenant = null)
    {
        var owner = tenant ?? _tenant;
        var user = User.Register(UserId.New(), owner, identifier, "Test " + identifier, role, _factory.PinHasher.Hash("1234"), LongAgo);
        await using var db = Db.App(fixture, owner);
        await new UserRepository(db).AddAsync(user, CancellationToken.None);
        await new EfUnitOfWork(db).CommitAsync(CancellationToken.None);
        return user;
    }

    private async Task SeedCommerceAsync(TenantId? tenant = null, string name = "Supérette du Carrefour")
    {
        var id = tenant ?? _tenant;
        var address = EmailAddress.Parse($"c{Guid.NewGuid():N}@exemple.ci");
        await using var db = Db.App(fixture, id);
        await new CommerceRepository(db).AddAsync(
            Commerce.Open(id, name, ActivityType.Shop, address, "2026-10-brouillon", DateTimeOffset.UtcNow), CancellationToken.None);
        await new EfUnitOfWork(db).CommitAsync(CancellationToken.None);
    }

    private HttpClient ClientFor(User user, TenantId? tenant = null) => _factory.ClientFor(tenant ?? _tenant, user.Id, user.Role);

    private async Task<string> InviteAsync(HttpClient client, string? name = "Caisse 1")
    {
        var response = await client.PostAsJsonAsync("/api/v1/devices/invitations", new { suggestedName = name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonOf(response)).GetProperty("token").GetString()!;
    }

    private async Task<(Guid Device, string Secret)> EnrollAsync(string token, string? name = null)
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/v1/devices/enroll", new { token, deviceName = name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        return (body.GetProperty("deviceId").GetGuid(), body.GetProperty("deviceSecret").GetString()!);
    }

    private async Task<HttpResponseMessage> LoginAsync(string identifier, Guid? device, string? secret, string pin = "1234", TenantId? tenant = null)
    {
        using var anonymous = _factory.CreateClient();
        return await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { tenantId = (tenant ?? _tenant).Value, identifier, pin, deviceId = device, deviceSecret = secret });
    }

    private HttpClient WithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task A_manager_invites_a_device_which_enrols_and_lets_a_cashier_log_in()
    {
        await SeedCommerceAsync();
        var manager = await SeedUserAsync("gerant", Role.Manager);
        await SeedUserAsync("awa", Role.Cashier);
        using var managerClient = ClientFor(manager);

        var token = await InviteAsync(managerClient);
        var (device, secret) = await EnrollAsync(token);
        var login = await LoginAsync("awa", device, secret);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var cashier = WithToken((await JsonOf(login)).GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/products")).StatusCode);
        var devices = await JsonOf(await managerClient.GetAsync("/api/v1/devices"));
        Assert.Equal(1, devices.GetArrayLength());
        Assert.Equal("Caisse 1", devices[0].GetProperty("name").GetString());
        Assert.True(devices[0].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task The_enrolment_answer_names_the_commerce_in_plain_words()
    {
        await SeedCommerceAsync(name: "Quincaillerie Koffi");
        using var manager = ClientFor(await SeedUserAsync("gerant", Role.Manager));
        var token = await InviteAsync(manager);

        using var anonymous = _factory.CreateClient();
        var body = await JsonOf(await anonymous.PostAsJsonAsync("/api/v1/devices/enroll", new { token, deviceName = "Caisse du fond" }));

        Assert.Equal("Quincaillerie Koffi", body.GetProperty("commerceName").GetString());
        Assert.Equal("Caisse du fond", body.GetProperty("deviceName").GetString());
        Assert.Equal(_tenant.Value, body.GetProperty("tenantId").GetGuid());
    }

    [Fact]
    public async Task A_cashier_cannot_invite_list_or_revoke()
    {
        await SeedCommerceAsync();
        using var cashier = ClientFor(await SeedUserAsync("awa", Role.Cashier));

        await AssertProblemAsync(await cashier.PostAsJsonAsync("/api/v1/devices/invitations", new { }), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(await cashier.GetAsync("/api/v1/devices"), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(await cashier.PostAsync($"/api/v1/devices/{Guid.NewGuid()}/revoke", null), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_token_works_once_and_the_second_try_says_so_in_plain_words()
    {
        await SeedCommerceAsync();
        using var manager = ClientFor(await SeedUserAsync("gerant", Role.Manager));
        var token = await InviteAsync(manager);
        await EnrollAsync(token);

        using var anonymous = _factory.CreateClient();
        var second = await anonymous.PostAsJsonAsync("/api/v1/devices/enroll", new { token });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var body = await JsonOf(second);
        Assert.Equal("invalid_invitation", body.GetProperty("code").GetString());
        Assert.Contains("déjà été utilisé", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_forged_or_foreign_token_is_refused_without_detail()
    {
        await SeedCommerceAsync();
        using var manager = ClientFor(await SeedUserAsync("gerant", Role.Manager));
        var token = await InviteAsync(manager);
        using var anonymous = _factory.CreateClient();

        foreach (var bad in new[] { "n'importe quoi", token[..70], token + "x", new string('0', 64) + "abc" })
        {
            var response = await anonymous.PostAsJsonAsync("/api/v1/devices/enroll", new { token = bad });
            await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "invalid_invitation");
        }

        using var empty = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsync("/api/v1/devices/enroll", empty)).StatusCode);
    }

    [Fact]
    public async Task Two_devices_presenting_the_same_token_at_once_enrol_only_one()
    {
        await SeedCommerceAsync();
        using var manager = ClientFor(await SeedUserAsync("gerant", Role.Manager));
        var token = await InviteAsync(manager);
        using var one = _factory.CreateClient();
        using var two = _factory.CreateClient();

        var responses = await Task.WhenAll(
            one.PostAsJsonAsync("/api/v1/devices/enroll", new { token }),
            two.PostAsJsonAsync("/api/v1/devices/enroll", new { token }));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK)); // SC-A4
        var devices = await JsonOf(await manager.GetAsync("/api/v1/devices"));
        Assert.Equal(1, devices.GetArrayLength());
    }

    [Fact]
    public async Task Without_a_device_a_cashier_cannot_log_in_and_the_message_says_what_to_do()
    {
        await SeedCommerceAsync();
        await SeedUserAsync("awa", Role.Cashier);

        var response = await LoginAsync("awa", null, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("device_not_authorized", body.GetProperty("code").GetString());
        Assert.Contains("Demandez un nouveau lien", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_wrong_device_secret_is_refused_and_does_not_count_against_the_account()
    {
        await SeedCommerceAsync();
        var manager = await SeedUserAsync("gerant", Role.Manager);
        await SeedUserAsync("awa", Role.Cashier);
        using var managerClient = ClientFor(manager);
        var (device, secret) = await EnrollAsync(await InviteAsync(managerClient));

        for (var i = 0; i < 8; i++)
        {
            await AssertProblemAsync(await LoginAsync("awa", device, "faux-secret", pin: "0000"), HttpStatusCode.Forbidden, "device_not_authorized");
        }

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync("awa", device, secret)).StatusCode); // SC-A5 : jamais verrouillé
    }

    [Fact]
    public async Task Revoking_a_device_cuts_its_token_at_once_and_blocks_new_logins_but_not_other_devices()
    {
        await SeedCommerceAsync();
        var manager = await SeedUserAsync("gerant", Role.Manager);
        await SeedUserAsync("awa", Role.Cashier);
        using var managerClient = ClientFor(manager);
        var (first, firstSecret) = await EnrollAsync(await InviteAsync(managerClient, "Caisse 1"));
        var (_, secondSecret) = await EnrollAsync(await InviteAsync(managerClient, "Caisse 2"));
        var secondDevice = (await JsonOf(await managerClient.GetAsync("/api/v1/devices"))).EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "Caisse 2").GetProperty("id").GetGuid();
        var token = (await JsonOf(await LoginAsync("awa", first, firstSecret))).GetProperty("accessToken").GetString()!;
        using var cashier = WithToken(token);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/products")).StatusCode);

        var revoked = await managerClient.PostAsync($"/api/v1/devices/{first}/revoke", null);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        await AssertProblemAsync(await cashier.GetAsync("/api/v1/products"), HttpStatusCode.Unauthorized, "device_not_authorized"); // SC-A6
        await AssertProblemAsync(await LoginAsync("awa", first, firstSecret), HttpStatusCode.Forbidden, "device_not_authorized");
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync("awa", secondDevice, secondSecret)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await managerClient.PostAsync($"/api/v1/devices/{first}/revoke", null)).StatusCode); // idempotent
    }

    [Fact]
    public async Task The_owner_logged_in_by_password_needs_no_device()
    {
        await SeedCommerceAsync();
        using var owner = ClientFor(await SeedUserAsync("proprio", Role.Owner));

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/devices")).StatusCode);
    }

    [Fact]
    public async Task Another_commerce_sees_nothing_and_cannot_revoke()
    {
        await SeedCommerceAsync();
        var manager = await SeedUserAsync("gerant", Role.Manager);
        using var managerClient = ClientFor(manager);
        var (device, _) = await EnrollAsync(await InviteAsync(managerClient));
        var other = TenantId.New();
        await SeedCommerceAsync(other, "Autre commerce");
        var otherManager = await SeedUserAsync("autre", Role.Manager, other);
        using var otherClient = ClientFor(otherManager, other);

        var list = await JsonOf(await otherClient.GetAsync("/api/v1/devices"));
        var revoke = await otherClient.PostAsync($"/api/v1/devices/{device}/revoke", null);

        Assert.Equal(0, list.GetArrayLength());
        await AssertProblemAsync(revoke, HttpStatusCode.NotFound, "not_found"); // SC-A7
        await using var asOther = Db.App(fixture, other);
        Assert.Equal(0, await asOther.Database.SqlQuery<long>($"SELECT count(*)::bigint AS \"Value\" FROM devices").SingleAsync());
        Assert.Equal(0, await asOther.Database.SqlQuery<long>($"SELECT count(*)::bigint AS \"Value\" FROM device_invitations").SingleAsync());
    }

    [Fact]
    public async Task Secrets_are_stored_only_as_hashes_and_the_token_secret_is_not_in_the_database()
    {
        await SeedCommerceAsync();
        using var manager = ClientFor(await SeedUserAsync("gerant", Role.Manager));
        var token = await InviteAsync(manager);
        var (_, deviceSecret) = await EnrollAsync(token);

        await using var asTenant = Db.App(fixture, _tenant);
        var hashes = await asTenant.Database.SqlQuery<string>($"SELECT secret_hash AS \"Value\" FROM devices").ToListAsync();
        var invitationHashes = await asTenant.Database.SqlQuery<string>($"SELECT secret_hash AS \"Value\" FROM device_invitations").ToListAsync();

        Assert.DoesNotContain(deviceSecret, hashes.Single());
        Assert.DoesNotContain(token[64..], invitationHashes.Single());
        Assert.DoesNotContain(token, invitationHashes.Single());
    }

    [Fact]
    public async Task Invitation_enrolment_and_revocation_appear_in_the_journal_under_devices()
    {
        await SeedCommerceAsync();
        var manager = await SeedUserAsync("gerant", Role.Manager);
        using var owner = ClientFor(await SeedUserAsync("proprio", Role.Owner));
        using var managerClient = ClientFor(manager);
        var (device, _) = await EnrollAsync(await InviteAsync(managerClient));
        await managerClient.PostAsync($"/api/v1/devices/{device}/revoke", null);

        var journal = await JsonOf(await owner.GetAsync("/api/v1/audit-log?category=devices"));

        var actions = journal.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("action").GetString()).ToList();
        Assert.Contains("device.invited", actions);
        Assert.Contains("device.enrolled", actions);
        Assert.Contains("device.revoked", actions);
    }

    [Fact]
    public async Task Enrolment_is_rate_limited_per_address()
    {
        using var limited = new ApiFactory(fixture, publicPermitsPerMinute: 3, devicesRequired: true);
        using var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/v1/devices/enroll", new { token = "x" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.UnprocessableEntity, statuses[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }
}
