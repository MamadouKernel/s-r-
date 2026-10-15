using Soro.Application.Common;
using Soro.Application.Sales;
using Soro.Domain.Common;

namespace Soro.Application.Tests;

public class SyncSalesTests
{
    [Fact]
    public async Task Processes_sales_in_sequence_order_whatever_the_arrival_order()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var batch = new SyncSalesCommand(
        [
            world.CashSale(session, sequence: 3),
            world.CashSale(session, sequence: 1),
            world.CashSale(session, sequence: 2),
        ]);

        var result = await world.SyncSales.HandleAsync(batch);

        Assert.Equal([1L, 2L, 3L], world.Sales.All.Select(sale => sale.LocalSequence));
        Assert.All(result.Statuses, status => Assert.Equal(SyncOutcome.Accepted, status.Outcome));
    }

    [Fact]
    public async Task Replaying_the_batch_creates_no_duplicate()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 10_000);
        var batch = new SyncSalesCommand(
            [world.CashSale(session, sequence: 1), world.CashSale(session, sequence: 2)]);
        await world.SyncSales.HandleAsync(batch);

        var replay = await world.SyncSales.HandleAsync(batch);

        Assert.Equal(2, world.Sales.All.Count);
        Assert.All(replay.Statuses, status => Assert.Equal(SyncOutcome.AlreadyProcessed, status.Outcome));
        Assert.Equal(Money.Fcfa(12_000), session.ExpectedCash);
    }

    [Fact]
    public async Task A_rejected_sale_is_reported_and_does_not_block_the_next_ones()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var unknownSession = world.CashSale(session, sequence: 2) with { Session = SessionId.New() };
        var batch = new SyncSalesCommand(
            [world.CashSale(session, sequence: 1), unknownSession, world.CashSale(session, sequence: 3)]);

        var result = await world.SyncSales.HandleAsync(batch);

        Assert.Equal(
            [SyncOutcome.Accepted, SyncOutcome.Rejected, SyncOutcome.Accepted],
            result.Statuses.Select(status => status.Outcome));
        Assert.Equal(ErrorCodes.NotFound, result.Statuses[1].ErrorCode);
        Assert.Equal(2, world.Sales.All.Count);
    }
}
