using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

public class ListUsersTests
{
    [Fact]
    public async Task A_manager_sees_the_whole_team_without_any_pin_hash()
    {
        var world = new TestWorld();
        var awa = world.GivenUser("awa", role: Role.Cashier);
        world.GivenUser("issa", role: Role.StockKeeper);
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.ListUsers.HandleAsync();

        Assert.True(result.IsSuccess);
        var team = result.Value!;
        Assert.Equal(2, team.Users.Count);
        var view = team.Users.Single(user => user.Id == awa.Id.Value);
        Assert.Equal("awa", view.Identifier);
        Assert.Equal(Role.Cashier, view.Role);
        Assert.Equal(UserStatus.Active, view.Status);
        // La vue n'a pas de champ de condensat : on le vérifie sur la forme même du type.
        Assert.DoesNotContain(typeof(UserView).GetProperties(), property => property.Name.Contains("Pin", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(UserView).GetProperties(), property => property.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Other_profiles_cannot_read_the_team(Role role)
    {
        var world = new TestWorld();
        world.ActAs(world.Cashier, role);

        var result = await world.ListUsers.HandleAsync();

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task Only_the_users_of_the_own_shop_are_listed()
    {
        var world = new TestWorld();
        world.GivenUser("awa", role: Role.Cashier);
        world.GivenUser("etranger", role: Role.Cashier, tenant: TenantId.New());
        world.ActAs(world.Manager, Role.Manager);

        var team = (await world.ListUsers.HandleAsync()).Value!;

        Assert.Equal(["awa"], team.Users.Select(user => user.Identifier));
    }

    [Fact]
    public async Task A_manager_can_disable_cashiers_and_stock_keepers_only()
    {
        var world = new TestWorld();
        world.GivenUser("awa", role: Role.Cashier);
        world.GivenUser("issa", role: Role.StockKeeper);
        world.GivenUser("autre.gerant", role: Role.Manager);
        world.GivenUser("proprio", role: Role.Owner);
        world.GivenUser("compta", role: Role.Accountant);
        world.ActAs(world.Manager, Role.Manager);

        var team = (await world.ListUsers.HandleAsync()).Value!;

        Assert.Equal(
            ["awa", "issa"],
            team.Users.Where(user => user.CanDisable).Select(user => user.Identifier).Order());
    }

    [Fact]
    public async Task An_owner_can_disable_everyone_but_another_owner_and_never_himself()
    {
        var world = new TestWorld();
        var self = world.GivenUser("proprio", role: Role.Owner);
        world.GivenUser("gerant", role: Role.Manager);
        world.GivenUser("autre.proprio", role: Role.Owner);
        world.GivenUser("compta", role: Role.Accountant);
        world.ActAs(self.Id, Role.Owner);

        var team = (await world.ListUsers.HandleAsync()).Value!;

        Assert.Equal(["compta", "gerant"], team.Users.Where(user => user.CanDisable).Select(user => user.Identifier).Order());
        var me = team.Users.Single(user => user.Identifier == "proprio");
        Assert.True(me.IsSelf);
        Assert.False(me.CanDisable);
    }

    [Fact]
    public async Task A_disabled_user_cannot_be_disabled_again()
    {
        var world = new TestWorld();
        var awa = world.GivenUser("awa", role: Role.Cashier);
        awa.Disable(world.Clock.UtcNow);
        world.ActAs(world.Manager, Role.Manager);

        var view = (await world.ListUsers.HandleAsync()).Value!.Users.Single();

        Assert.Equal(UserStatus.Disabled, view.Status);
        Assert.False(view.CanDisable);
    }

    [Fact]
    public async Task The_assignable_roles_follow_what_the_actor_may_create()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var asManager = (await world.ListUsers.HandleAsync()).Value!.AssignableRoles;

        world.ActAs(world.Owner, Role.Owner);
        var asOwner = (await world.ListUsers.HandleAsync()).Value!.AssignableRoles;

        Assert.Equal([Role.Cashier, Role.StockKeeper], asManager);
        Assert.Equal([Role.Manager, Role.Cashier, Role.StockKeeper, Role.Accountant], asOwner);
    }

    [Fact]
    public async Task Active_users_come_first_then_by_name()
    {
        var world = new TestWorld();
        var zoe = world.GivenUser("zoe", role: Role.Cashier);
        var abou = world.GivenUser("abou", role: Role.Cashier);
        var ancien = world.GivenUser("ancien", role: Role.Cashier);
        ancien.Disable(world.Clock.UtcNow);
        world.ActAs(world.Manager, Role.Manager);

        var team = (await world.ListUsers.HandleAsync()).Value!;

        Assert.Equal(["abou", "zoe", "ancien"], team.Users.Select(user => user.Identifier));
        Assert.NotNull(zoe);
        Assert.NotNull(abou);
    }

    [Fact]
    public async Task A_locked_account_shows_until_when()
    {
        var world = new TestWorld();
        var awa = world.GivenUser("awa", role: Role.Cashier);
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            awa.RecordFailedAttempt(world.Clock.UtcNow);
        }

        world.ActAs(world.Manager, Role.Manager);
        var view = (await world.ListUsers.HandleAsync()).Value!.Users.Single();

        Assert.Equal(world.Clock.UtcNow + User.LockDuration, view.LockedUntil);
    }

    [Fact]
    public async Task The_directory_gives_names_only_to_the_stock_profiles()
    {
        var world = new TestWorld();
        var awa = world.GivenUser("awa", role: Role.Cashier);
        var ancien = world.GivenUser("ancien", role: Role.Cashier);
        ancien.Disable(world.Clock.UtcNow);
        world.GivenUser("etranger", role: Role.Cashier, tenant: TenantId.New());

        world.ActAs(world.Cashier, Role.StockKeeper);
        var allowed = await world.GetDirectory.HandleAsync();
        world.ActAs(world.Cashier, Role.Cashier);
        var refused = await world.GetDirectory.HandleAsync();

        Assert.True(allowed.IsSuccess);
        Assert.Equal(2, allowed.Value!.Count);
        Assert.Contains(allowed.Value, entry => entry.Id == awa.Id.Value && entry.DisplayName == "Awa Koné");
        Assert.Contains(allowed.Value, entry => entry.Id == ancien.Id.Value);
        Assert.Equal(ErrorCodes.Forbidden, refused.Error!.Code);
    }
}
