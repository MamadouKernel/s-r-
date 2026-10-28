using Soro.Application.Cash;
using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

public class CloseRegisterSessionTests
{
    private static readonly DateTimeOffset Evening = TestWorld.Morning.AddHours(8);

    [Fact]
    public async Task Closes_the_session_and_reports_the_variance_to_a_manager()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 10_000);
        await world.RecordSale.HandleAsync(world.CashSale(session, price: 2_000));
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.CloseSession.HandleAsync(
            new CloseRegisterSessionCommand(session.Id, 11_500, Evening));

        Assert.True(result.IsSuccess);
        Assert.Equal(12_000, result.Value!.ExpectedCashFcfa);
        Assert.Equal(11_500, result.Value.CountedCashFcfa);
        Assert.Equal(-500, result.Value.VarianceFcfa);
        Assert.Equal(SessionStatus.Closed, session.Status);
    }

    [Fact]
    public async Task Fails_when_the_session_does_not_exist()
    {
        var world = new TestWorld();

        var result = await world.CloseSession.HandleAsync(
            new CloseRegisterSessionCommand(SessionId.New(), 0, Evening));

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Refuses_another_cashier_but_accepts_a_manager()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(cashier: UserId.New());
        var command = new CloseRegisterSessionCommand(session.Id, 10_000, Evening);

        var refused = await world.CloseSession.HandleAsync(command);
        world.ActAs(world.Manager, Role.Manager);
        var accepted = await world.CloseSession.HandleAsync(command);

        Assert.Equal(ErrorCodes.Forbidden, refused.Error!.Code);
        Assert.True(accepted.IsSuccess);
    }

    [Fact]
    public async Task Replaying_the_same_closing_returns_the_same_figures()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var command = new CloseRegisterSessionCommand(session.Id, 10_200, Evening);
        await world.CloseSession.HandleAsync(command);
        world.ActAs(world.Manager, Role.Manager);

        var replay = await world.CloseSession.HandleAsync(command);

        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Equal(200, replay.Value.VarianceFcfa);
    }

    [Fact]
    public async Task Refuses_a_different_count_on_an_already_closed_session()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        await world.CloseSession.HandleAsync(new CloseRegisterSessionCommand(session.Id, 10_000, Evening));

        var result = await world.CloseSession.HandleAsync(
            new CloseRegisterSessionCommand(session.Id, 9_000, Evening));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
    }
}
