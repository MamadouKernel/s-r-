using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Reporting;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

public class ReportingTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private static ListSalesQuery Sales(
        DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null, int? offset = null) =>
        new(from, to, null, null, true, limit, offset);

    [Theory]
    [InlineData(Role.Owner)]
    [InlineData(Role.Manager)]
    [InlineData(Role.Accountant)]
    public async Task Owner_manager_and_accountant_can_read(Role role)
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, role);

        Assert.True((await world.ListSales.HandleAsync(Sales())).IsSuccess);
        Assert.True((await world.ListSessions.HandleAsync(new ListSessionsQuery(null, null, null, false, null, null))).IsSuccess);
        Assert.True((await world.ActivityReport.HandleAsync(new GetActivityReportQuery(Start, Start.AddDays(1)))).IsSuccess);
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    public async Task Cashiers_and_stock_keepers_are_refused_everywhere(Role role)
    {
        var world = new TestWorld();
        world.ActAs(world.Cashier, role);

        // Un caissier retrouve ses propres tickets (TillSalesTests) ; tout le reste des rapports lui est fermé.
        var results = new List<ApplicationError?>
        {
            (await world.ListSessions.HandleAsync(new ListSessionsQuery(null, null, null, false, null, null))).Error,
            (await world.ActivityReport.HandleAsync(new GetActivityReportQuery(Start, Start.AddDays(1)))).Error,
        };
        if (role != Role.Cashier)
        {
            results.Add((await world.ListSales.HandleAsync(Sales())).Error);
            results.Add((await world.GetSale.HandleAsync(SaleId.New())).Error);
        }

        Assert.All(results, error => Assert.Equal(ErrorCodes.Forbidden, error!.Code));
        Assert.Empty(world.Reporting.Calls);
    }

    [Fact]
    public async Task The_current_tenant_is_always_passed_to_the_queries()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);

        await world.ListSales.HandleAsync(Sales());

        Assert.Equal(world.Tenant, world.Reporting.LastTenant);
    }

    [Fact]
    public async Task An_inverted_or_too_long_period_is_a_rule_violation()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);

        var inverted = await world.ListSales.HandleAsync(Sales(Start.AddDays(1), Start));
        var tooLong = await world.ActivityReport.HandleAsync(new GetActivityReportQuery(Start, Start.AddDays(367)));
        var maximum = await world.ActivityReport.HandleAsync(new GetActivityReportQuery(Start, Start.AddDays(366)));

        Assert.Equal(ErrorCodes.RuleViolation, inverted.Error!.Code);
        Assert.Equal(ErrorCodes.RuleViolation, tooLong.Error!.Code);
        Assert.True(maximum.IsSuccess);
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData(0, 1)]
    [InlineData(10, 10)]
    [InlineData(5_000, 200)]
    public async Task The_page_size_is_defaulted_and_capped(int? requested, int expected)
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);

        await world.ListSales.HandleAsync(Sales(limit: requested));

        Assert.Equal(expected, world.Reporting.LastSalesFilter!.Limit);
    }

    [Fact]
    public async Task A_negative_offset_becomes_zero()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);

        await world.ListSales.HandleAsync(Sales(offset: -5));

        Assert.Equal(0, world.Reporting.LastSalesFilter!.Offset);
    }

    [Fact]
    public async Task An_unknown_sale_is_not_found()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.GetSale.HandleAsync(SaleId.New());

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
    }
}
