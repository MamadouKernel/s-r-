using Soro.Application.Common;
using Soro.Application.Reporting;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Sales;

namespace Soro.Application.Tests;

/// <summary>Retrouver un ticket à la caisse (docs/specs/annulations-remises-retours ARR-09) : le caissier voit ses ventes des sept derniers jours, le gérant toutes.</summary>
public class TillSalesTests
{
    private static SaleDetail DetailOf(Guid cashier) =>
        new(Guid.NewGuid(), TestWorld.Morning, cashier, Guid.NewGuid(), Guid.NewGuid(), 2_000, 0, 2_000, SaleStatus.Validated, null, [], [], null, null, 7);

    [Fact]
    public async Task A_cashier_lists_only_his_own_sales_of_the_last_seven_days_whatever_he_asks()
    {
        var world = new TestWorld();
        var longAgo = world.Clock.UtcNow.AddDays(-40);

        var result = await world.ListSales.HandleAsync(new ListSalesQuery(longAgo, null, null, new UserId(Guid.NewGuid()), true, 500, 0));

        Assert.True(result.IsSuccess);
        var filter = world.Reporting.LastSalesFilter!;
        Assert.Equal(world.Cashier, filter.Cashier);
        Assert.True(filter.From >= world.Clock.UtcNow.AddDays(-7).AddMinutes(-1));
    }

    [Fact]
    public async Task A_manager_keeps_the_filters_he_chose()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);

        await world.ListSales.HandleAsync(new ListSalesQuery(null, null, null, null, true, null, null));

        Assert.Null(world.Reporting.LastSalesFilter!.Cashier);
    }

    [Fact]
    public async Task A_cashier_cannot_open_the_detail_of_a_colleagues_sale_but_can_open_his_own()
    {
        var world = new TestWorld();
        world.Reporting.Detail = DetailOf(Guid.NewGuid());
        var colleague = await world.GetSale.HandleAsync(world.Reporting.Detail.Id is var id ? new SaleId(id) : default);
        world.Reporting.Detail = DetailOf(world.Cashier.Value);
        var own = await world.GetSale.HandleAsync(new SaleId(world.Reporting.Detail.Id));

        Assert.Equal(ErrorCodes.NotFound, colleague.Error!.Code);
        Assert.True(own.IsSuccess);
        Assert.Equal(7, own.Value!.Ticket);
    }

    [Fact]
    public async Task A_stock_keeper_has_no_access_to_sales()
    {
        var world = new TestWorld();
        world.ActAs(world.Cashier, Role.StockKeeper);

        var list = await world.ListSales.HandleAsync(new ListSalesQuery(null, null, null, null, true, null, null));

        Assert.Equal(ErrorCodes.Forbidden, list.Error!.Code);
    }
}
