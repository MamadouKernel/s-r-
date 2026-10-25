using Soro.Application.Common;
using Soro.Application.Reporting;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

public class TopProductsTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private static GetTopProductsQuery Query(
        RankingCriterion by = RankingCriterion.Quantity, int? limit = null, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        new(from ?? Start, to ?? Start.AddDays(7), by, limit);

    [Theory]
    [InlineData(Role.Owner)]
    [InlineData(Role.Manager)]
    [InlineData(Role.Accountant)]
    public async Task The_report_readers_can_rank_by_quantity_and_revenue(Role role)
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, role);

        Assert.True((await world.TopProducts.HandleAsync(Query(RankingCriterion.Quantity))).IsSuccess);
        Assert.True((await world.TopProducts.HandleAsync(Query(RankingCriterion.Revenue))).IsSuccess);
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    public async Task Other_profiles_are_refused(Role role)
    {
        var world = new TestWorld();
        world.ActAs(world.Cashier, role);

        var result = await world.TopProducts.HandleAsync(Query());

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Empty(world.Reporting.Calls);
    }

    [Fact]
    public async Task The_margin_is_for_the_profiles_that_see_purchase_prices_only()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Accountant);
        var refused = await world.TopProducts.HandleAsync(Query(RankingCriterion.Margin));
        Assert.Equal(ErrorCodes.Forbidden, refused.Error!.Code);
        Assert.Empty(world.Reporting.Calls);

        world.ActAs(world.Manager, Role.Owner);
        var allowed = await world.TopProducts.HandleAsync(Query(RankingCriterion.Margin));
        Assert.True(allowed.IsSuccess);
        Assert.True(world.Reporting.LastIncludeMargin);
        Assert.Equal(RankingCriterion.Margin, world.Reporting.LastCriterion);
    }

    [Fact]
    public async Task An_accountant_ranking_never_asks_for_the_margin()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Accountant);

        await world.TopProducts.HandleAsync(Query(RankingCriterion.Revenue));

        Assert.False(world.Reporting.LastIncludeMargin);
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData(0, 1)]
    [InlineData(5, 5)]
    [InlineData(500, 50)]
    public async Task The_limit_is_defaulted_and_capped(int? requested, int expected)
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);

        await world.TopProducts.HandleAsync(Query(limit: requested));

        Assert.Equal(expected, world.Reporting.LastLimit);
    }

    [Fact]
    public async Task An_inverted_or_too_long_period_is_a_rule_violation_and_the_tenant_is_always_passed()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);

        var inverted = await world.TopProducts.HandleAsync(Query(from: Start.AddDays(1), to: Start));
        var tooLong = await world.TopProducts.HandleAsync(Query(to: Start.AddDays(400)));
        await world.TopProducts.HandleAsync(Query());

        Assert.Equal(ErrorCodes.RuleViolation, inverted.Error!.Code);
        Assert.Equal(ErrorCodes.RuleViolation, tooLong.Error!.Code);
        Assert.Equal(world.Tenant, world.Reporting.LastTenant);
    }
}

public class AuditLogTests
{
    private static ListAuditLogQuery Query(string? category = null, int? limit = null, int? offset = null) =>
        new(null, null, category, limit, offset);

    [Theory]
    [InlineData(Role.Owner)]
    [InlineData(Role.Accountant)]
    public async Task The_owner_and_the_accountant_read_the_journal(Role role)
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, role);

        var result = await world.ListAuditLog.HandleAsync(Query());

        Assert.True(result.IsSuccess);
        Assert.Equal(world.Tenant, world.AuditLog.LastTenant);
    }

    [Theory]
    [InlineData(Role.Manager)]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    public async Task The_profiles_the_journal_watches_cannot_read_it(Role role)
    {
        var world = new TestWorld();
        world.ActAs(world.Cashier, role);

        var result = await world.ListAuditLog.HandleAsync(Query());

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Equal(0, world.AuditLog.Calls);
    }

    [Theory]
    [InlineData("sales", "sale.")]
    [InlineData("SALES", "sale.")]
    [InlineData("stock", "stock.")]
    [InlineData("team", "user.")]
    [InlineData("subscription", "subscription.")]
    public async Task A_category_becomes_its_action_prefixes(string category, string prefix)
    {
        var world = new TestWorld();
        world.ActAs(world.Owner, Role.Owner);

        await world.ListAuditLog.HandleAsync(Query(category));

        Assert.Equal([prefix], world.AuditLog.LastFilter!.ActionPrefixes);
    }

    [Fact]
    public async Task The_catalogue_category_covers_products_families_and_imports()
    {
        var world = new TestWorld();
        world.ActAs(world.Owner, Role.Owner);

        await world.ListAuditLog.HandleAsync(Query("catalogue"));

        Assert.Equal(["product.", "products.", "family."], world.AuditLog.LastFilter!.ActionPrefixes);
    }

    [Fact]
    public async Task No_category_means_every_action_and_an_unknown_one_is_refused()
    {
        var world = new TestWorld();
        world.ActAs(world.Owner, Role.Owner);

        await world.ListAuditLog.HandleAsync(Query());
        Assert.Null(world.AuditLog.LastFilter!.ActionPrefixes);

        var unknown = await world.ListAuditLog.HandleAsync(Query("secrets"));
        Assert.Equal(ErrorCodes.RuleViolation, unknown.Error!.Code);
        Assert.Contains("sales", unknown.Error.Message);
        Assert.Equal(1, world.AuditLog.Calls);
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData(0, 1)]
    [InlineData(20, 20)]
    [InlineData(5_000, 200)]
    public async Task The_page_size_is_defaulted_and_capped(int? requested, int expected)
    {
        var world = new TestWorld();
        world.ActAs(world.Owner, Role.Owner);

        await world.ListAuditLog.HandleAsync(Query(limit: requested));

        Assert.Equal(expected, world.AuditLog.LastFilter!.Limit);
    }

    [Fact]
    public async Task A_negative_offset_becomes_zero_and_a_bad_period_is_refused()
    {
        var world = new TestWorld();
        world.ActAs(world.Owner, Role.Owner);

        await world.ListAuditLog.HandleAsync(Query(offset: -3));
        Assert.Equal(0, world.AuditLog.LastFilter!.Offset);

        var inverted = await world.ListAuditLog.HandleAsync(new ListAuditLogQuery(
            new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero), null, null, null));
        Assert.Equal(ErrorCodes.RuleViolation, inverted.Error!.Code);
    }

    [Fact]
    public async Task The_directory_now_serves_the_accountant_but_still_not_the_cashier()
    {
        var world = new TestWorld();
        world.ActAs(world.Cashier, Role.Accountant);
        Assert.True((await world.GetDirectory.HandleAsync()).IsSuccess);

        world.ActAs(world.Cashier, Role.Cashier);
        Assert.Equal(ErrorCodes.Forbidden, (await world.GetDirectory.HandleAsync()).Error!.Code);
    }
}
