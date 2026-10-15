using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

public class DisableUserTests
{
    [Fact]
    public async Task The_owner_disables_a_cashier_and_it_is_audited()
    {
        var world = new TestWorld();
        var cashier = world.GivenUser(role: Role.Cashier);
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.DisableUser.HandleAsync(new DisableUserCommand(cashier.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Disabled, cashier.Status);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.UserDisabled);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Theory]
    [InlineData(Role.Manager, Role.Manager)]
    [InlineData(Role.Cashier, Role.Cashier)]
    [InlineData(Role.Manager, Role.Owner)]
    public async Task Refuses_when_the_actor_may_not_manage_that_profile(Role actor, Role target)
    {
        var world = new TestWorld();
        var victim = world.GivenUser(role: target);
        world.ActAs(UserId.New(), actor);

        var result = await world.DisableUser.HandleAsync(new DisableUserCommand(victim.Id));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Equal(UserStatus.Active, victim.Status);
    }

    [Fact]
    public async Task A_manager_may_disable_a_cashier()
    {
        var world = new TestWorld();
        var cashier = world.GivenUser(role: Role.Cashier);
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.DisableUser.HandleAsync(new DisableUserCommand(cashier.Id));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Fails_for_an_unknown_user_or_one_of_another_commerce()
    {
        var world = new TestWorld();
        var foreign = world.GivenUser(tenant: TenantId.New());
        world.ActAs(world.Owner, Role.Owner);

        var unknown = await world.DisableUser.HandleAsync(new DisableUserCommand(UserId.New()));
        var other = await world.DisableUser.HandleAsync(new DisableUserCommand(foreign.Id));

        Assert.Equal(ErrorCodes.NotFound, unknown.Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, other.Error!.Code);
    }

    [Fact]
    public async Task Disabling_twice_is_acknowledged()
    {
        var world = new TestWorld();
        var cashier = world.GivenUser(role: Role.Cashier);
        world.ActAs(world.Owner, Role.Owner);
        await world.DisableUser.HandleAsync(new DisableUserCommand(cashier.Id));

        var again = await world.DisableUser.HandleAsync(new DisableUserCommand(cashier.Id));

        Assert.True(again.IsSuccess);
    }
}
