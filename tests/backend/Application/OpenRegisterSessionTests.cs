using Soro.Application.Cash;
using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

public class OpenRegisterSessionTests
{
    private static OpenRegisterSessionCommand Command(RegisterId? register = null, long openingFloat = 10_000) =>
        new(SessionId.New(), PointOfSaleId.New(), register ?? RegisterId.New(), openingFloat, TestWorld.Morning);

    [Fact]
    public async Task Opens_a_session_for_the_current_cashier()
    {
        var world = new TestWorld();
        var command = Command();

        var result = await world.OpenSession.HandleAsync(command);

        Assert.True(result.IsSuccess);
        var stored = await world.Sessions.GetAsync(command.SessionId, world.Tenant, CancellationToken.None);
        Assert.Equal(world.Cashier, stored!.Assignment.Cashier);
        Assert.Equal(Money.Fcfa(10_000), stored.OpeningFloat);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Refuses_a_user_who_cannot_operate_a_register()
    {
        var world = new TestWorld();
        world.ActAs(UserId.New(), Role.StockKeeper);

        var result = await world.OpenSession.HandleAsync(Command());

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Equal(0, world.Sessions.Count);
    }

    [Fact]
    public async Task Refuses_a_second_open_session_on_the_same_register()
    {
        var world = new TestWorld();
        var register = RegisterId.New();
        await world.OpenSession.HandleAsync(Command(register));

        var result = await world.OpenSession.HandleAsync(Command(register));

        Assert.Equal(ErrorCodes.RegisterAlreadyOpen, result.Error!.Code);
        Assert.Equal(1, world.Sessions.Count);
    }

    [Fact]
    public async Task Replaying_the_same_session_is_acknowledged_without_a_duplicate()
    {
        var world = new TestWorld();
        var command = Command();
        await world.OpenSession.HandleAsync(command);

        var replay = await world.OpenSession.HandleAsync(command);

        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Equal(1, world.Sessions.Count);
    }

    [Fact]
    public async Task Reports_a_negative_opening_float_as_a_rule_violation()
    {
        var world = new TestWorld();

        var result = await world.OpenSession.HandleAsync(Command(openingFloat: -5));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Equal(0, world.Sessions.Count);
    }
}
