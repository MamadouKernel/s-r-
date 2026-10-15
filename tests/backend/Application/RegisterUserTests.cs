using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

public class RegisterUserTests
{
    private static RegisterUserCommand Command(
        Role role = Role.Cashier,
        string identifier = "awa.kone",
        string pin = "1234",
        UserId? id = null) =>
        new(id ?? UserId.New(), identifier, "Awa Koné", role, pin);

    [Fact]
    public async Task The_owner_registers_a_cashier_and_only_the_pin_hash_is_stored()
    {
        var world = new TestWorld();
        world.ActAs(world.Owner, Role.Owner);
        var command = Command();

        var result = await world.RegisterUser.HandleAsync(command);

        Assert.True(result.IsSuccess);
        var stored = await world.Users.GetAsync(command.Id, world.Tenant, CancellationToken.None);
        Assert.Equal(Role.Cashier, stored!.Role);
        Assert.Equal(world.Tenant, stored.Tenant);
        Assert.Equal(world.Hasher.Hash("1234"), stored.PinHash);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Theory]
    [InlineData(Role.Owner, Role.Manager, true)]
    [InlineData(Role.Owner, Role.Accountant, true)]
    [InlineData(Role.Owner, Role.Owner, false)]
    [InlineData(Role.Manager, Role.Cashier, true)]
    [InlineData(Role.Manager, Role.StockKeeper, true)]
    [InlineData(Role.Manager, Role.Manager, false)]
    [InlineData(Role.Cashier, Role.Cashier, false)]
    [InlineData(Role.StockKeeper, Role.Cashier, false)]
    [InlineData(Role.Accountant, Role.Cashier, false)]
    public async Task Who_may_register_which_profile(Role actor, Role target, bool allowed)
    {
        var world = new TestWorld();
        world.ActAs(UserId.New(), actor);

        var result = await world.RegisterUser.HandleAsync(Command(role: target));

        Assert.Equal(allowed, result.IsSuccess);
        if (!allowed)
        {
            Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
            Assert.Equal(0, world.Users.Count);
        }
    }

    [Fact]
    public async Task Refuses_an_identifier_already_used_in_the_commerce()
    {
        var world = new TestWorld();
        world.GivenUser(identifier: "awa.kone");
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.RegisterUser.HandleAsync(Command(identifier: "AWA.kone"));

        Assert.Equal(ErrorCodes.IdentifierTaken, result.Error!.Code);
    }

    [Fact]
    public async Task The_same_identifier_may_exist_in_another_commerce()
    {
        var world = new TestWorld();
        world.GivenUser(identifier: "awa.kone", tenant: TenantId.New());
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.RegisterUser.HandleAsync(Command(identifier: "awa.kone"));

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("12a4")]
    [InlineData("")]
    public async Task Refuses_a_pin_that_is_not_four_to_six_digits(string pin)
    {
        var world = new TestWorld();
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.RegisterUser.HandleAsync(Command(pin: pin));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Equal(0, world.Users.Count);
    }

    [Fact]
    public async Task Reports_an_invalid_identifier_as_a_rule_violation()
    {
        var world = new TestWorld();
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.RegisterUser.HandleAsync(Command(identifier: "no spaces"));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
    }

    [Fact]
    public async Task Replaying_the_same_registration_is_acknowledged()
    {
        var world = new TestWorld();
        world.ActAs(world.Owner, Role.Owner);
        var command = Command();
        await world.RegisterUser.HandleAsync(command);

        var replay = await world.RegisterUser.HandleAsync(command);

        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Equal(1, world.Users.Count);
    }
}
