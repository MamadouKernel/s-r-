using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

[Collection(Postgres.Collection)]
public sealed class TeamTests(PostgresFixture fixture) : IDisposable
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset LongAgo = DateTimeOffset.UtcNow.AddDays(-30);

    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private async Task<User> SeedAsync(string identifier, Role role, TenantId? tenant = null, string name = "Test")
    {
        var owner = tenant ?? _tenant;
        var user = User.Register(UserId.New(), owner, identifier, name + " " + identifier, role, _factory.PinHasher.Hash("1234"), LongAgo);
        await using var db = Db.App(fixture, owner);
        await new UserRepository(db).AddAsync(user, Ct);
        await new EfUnitOfWork(db).CommitAsync(Ct);
        return user;
    }

    [Fact]
    public async Task The_repository_lists_only_the_users_of_the_tenant()
    {
        await SeedAsync("awa", Role.Cashier);
        await SeedAsync("issa", Role.StockKeeper);
        var other = TenantId.New();
        await SeedAsync("etranger", Role.Cashier, other);

        await using var db = Db.App(fixture, _tenant);
        var listed = await new UserRepository(db).ListAsync(_tenant, Ct);
        var foreign = await new UserRepository(db).ListAsync(other, Ct);

        Assert.Equal(["awa", "issa"], listed.Select(user => user.Identifier).Order());
        Assert.Empty(foreign); // la sécurité par ligne de la base masque l'autre commerce même si on le demande
    }

    [Fact]
    public async Task The_api_lists_the_team_for_a_manager_with_the_right_flags_and_never_a_pin_hash()
    {
        var manager = await SeedAsync("gerant", Role.Manager);
        var awa = await SeedAsync("awa", Role.Cashier);
        await SeedAsync("proprio", Role.Owner);
        using var client = _factory.ClientFor(_tenant, manager.Id, Role.Manager);

        var response = await client.GetAsync("/api/v1/users");
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = body.GetProperty("users").EnumerateArray().ToList();
        Assert.Equal(3, users.Count);
        var awaView = users.Single(user => user.GetProperty("identifier").GetString() == "awa");
        Assert.Equal(awa.Id.Value.ToString(), awaView.GetProperty("id").GetString());
        Assert.Equal("Cashier", awaView.GetProperty("role").GetString());
        Assert.Equal("Active", awaView.GetProperty("status").GetString());
        Assert.True(awaView.GetProperty("canDisable").GetBoolean());
        Assert.False(users.Single(user => user.GetProperty("identifier").GetString() == "proprio").GetProperty("canDisable").GetBoolean());
        Assert.True(users.Single(user => user.GetProperty("identifier").GetString() == "gerant").GetProperty("isSelf").GetBoolean());
        Assert.Equal(["Cashier", "StockKeeper"], body.GetProperty("assignableRoles").EnumerateArray().Select(role => role.GetString()));

        // SC-E5 : aucun condensat, aucun mot lié au PIN dans la réponse.
        Assert.DoesNotContain("pbkdf2", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pin", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task The_api_refuses_the_team_to_the_other_profiles(Role role)
    {
        using var client = _factory.ClientFor(_tenant, UserId.New(), role);

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Another_shop_never_sees_these_users()
    {
        var awa = await SeedAsync("awa", Role.Cashier);
        using var stranger = _factory.ClientFor(TenantId.New(), UserId.New(), Role.Owner);

        var team = await stranger.GetFromJsonAsync<JsonElement>("/api/v1/users");
        var directory = await stranger.GetFromJsonAsync<JsonElement>("/api/v1/users/directory");

        // Seul l'utilisateur de test de cet autre commerce y figure : aucun membre du premier.
        Assert.DoesNotContain(team.GetProperty("users").EnumerateArray(), user => user.GetProperty("id").GetGuid() == awa.Id.Value);
        Assert.DoesNotContain(directory.EnumerateArray(), entry => entry.GetProperty("id").GetGuid() == awa.Id.Value);
    }

    [Fact]
    public async Task The_directory_gives_names_to_the_stock_keeper_but_not_to_the_cashier()
    {
        var awa = await SeedAsync("awa", Role.Cashier, name: "Awa");
        var ancien = await SeedAsync("ancien", Role.Cashier, name: "Ancien");
        using var manager = _factory.ClientFor(_tenant, UserId.New(), Role.Manager);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsync($"/api/v1/users/{ancien.Id.Value}/disable", null)).StatusCode);
        using var keeper = _factory.ClientFor(_tenant, UserId.New(), Role.StockKeeper);
        using var cashier = _factory.ClientFor(_tenant, UserId.New(), Role.Cashier);

        var directory = await keeper.GetFromJsonAsync<JsonElement>("/api/v1/users/directory");
        var refused = await cashier.GetAsync("/api/v1/users/directory");

        var entries = directory.EnumerateArray().ToList();
        Assert.Contains(entries, entry => entry.GetProperty("id").GetString() == ancien.Id.Value.ToString()); // l'ancien employé reste l'auteur de mouvements passés
        Assert.Contains(entries, entry => entry.GetProperty("id").GetString() == awa.Id.Value.ToString() && entry.GetProperty("displayName").GetString() == "Awa awa");
        Assert.DoesNotContain(entries, entry => entry.TryGetProperty("identifier", out _));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task A_disabled_user_is_listed_as_disabled_and_cannot_be_disabled_again()
    {
        var awa = await SeedAsync("awa", Role.Cashier);
        using var manager = _factory.ClientFor(_tenant, UserId.New(), Role.Manager);
        await manager.PostAsync($"/api/v1/users/{awa.Id.Value}/disable", null);

        var team = await manager.GetFromJsonAsync<JsonElement>("/api/v1/users");

        var view = Assert.Single(team.GetProperty("users").EnumerateArray(), user => user.GetProperty("id").GetGuid() == awa.Id.Value);
        Assert.Equal("Disabled", view.GetProperty("status").GetString());
        Assert.False(view.GetProperty("canDisable").GetBoolean());
    }
}
