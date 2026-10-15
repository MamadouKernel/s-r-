using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Application.Tests;

public class RecordSaleTests
{
    [Fact]
    public async Task Records_a_cash_sale_and_adds_the_cash_to_the_session()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 10_000);

        var result = await world.RecordSale.HandleAsync(world.CashSale(session, price: 2_500, tendered: 3_000));

        Assert.True(result.IsSuccess);
        Assert.Equal(2_500, result.Value!.NetTotalFcfa);
        Assert.Equal(500, result.Value.ChangeFcfa);
        Assert.Single(world.Sales.All);
        Assert.Equal(Money.Fcfa(12_500), session.ExpectedCash);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Fails_when_the_session_does_not_exist()
    {
        var world = new TestWorld();
        var command = world.CashSale(world.GivenOpenSession()) with { Session = SessionId.New() };

        var result = await world.RecordSale.HandleAsync(command);

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Does_not_reveal_a_session_of_another_tenant()
    {
        var world = new TestWorld();
        var foreignSession = world.GivenOpenSession(tenant: TenantId.New());

        var result = await world.RecordSale.HandleAsync(world.CashSale(foreignSession));

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
        Assert.Empty(world.Sales.All);
    }

    [Fact]
    public async Task Refuses_to_record_on_another_cashiers_session()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(cashier: UserId.New());

        var result = await world.RecordSale.HandleAsync(world.CashSale(session));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task Refuses_a_user_who_cannot_operate_a_register()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        world.ActAs(UserId.New(), Role.StockKeeper);

        var result = await world.RecordSale.HandleAsync(world.CashSale(session));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task Replaying_the_same_sale_is_acknowledged_without_a_duplicate()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 10_000);
        var command = world.CashSale(session, price: 1_000);

        await world.RecordSale.HandleAsync(command);
        var replay = await world.RecordSale.HandleAsync(command);

        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Single(world.Sales.All);
        Assert.Equal(Money.Fcfa(11_000), session.ExpectedCash);
    }

    [Fact]
    public async Task Rejects_a_mobile_money_reference_that_was_already_used()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        await world.RecordSale.HandleAsync(world.MobileSale(session, "MP-100"));

        var result = await world.RecordSale.HandleAsync(world.MobileSale(session, "mp-100", sequence: 2));

        Assert.Equal(ErrorCodes.MobileMoneyReferenceUsed, result.Error!.Code);
        Assert.Single(world.Sales.All);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Accepts_the_same_reference_for_another_operator()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        await world.RecordSale.HandleAsync(world.MobileSale(session, "MP-100"));

        var result = await world.RecordSale.HandleAsync(
            world.MobileSale(session, "MP-100", PaymentMethod.MtnMomo, sequence: 2));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Refuses_a_large_discount_without_approval_and_audits_the_attempt()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();

        var result = await world.RecordSale.HandleAsync(world.CashSale(session, price: 1_000, discount: 200));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Empty(world.Sales.All);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.DiscountRefused);
    }

    [Fact]
    public async Task Accepts_a_large_discount_approved_by_a_manager()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();

        var result = await world.RecordSale.HandleAsync(
            world.CashSale(session, price: 1_000, discount: 200, approver: world.Manager));

        Assert.True(result.IsSuccess);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.DiscountApproved);
    }

    [Fact]
    public async Task Refuses_an_approval_given_by_a_non_manager()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var colleague = UserId.New();
        world.Directory.Set(world.Tenant, colleague, Role.Cashier);

        var result = await world.RecordSale.HandleAsync(
            world.CashSale(session, price: 1_000, discount: 200, approver: colleague));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task A_discount_at_the_threshold_needs_no_approval()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();

        var result = await world.RecordSale.HandleAsync(world.CashSale(session, price: 1_000, discount: 100));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_manager_can_grant_a_large_discount_without_a_second_approver()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.RecordSale.HandleAsync(world.CashSale(session, price: 1_000, discount: 300));

        Assert.True(result.IsSuccess);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.DiscountApproved);
    }

    [Fact]
    public async Task Reports_a_business_rule_violation_and_saves_nothing()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var command = world.CashSale(session, price: 1_000) with
        {
            Payments = [new PaymentInput(PaymentId.New(), PaymentMethod.Cash, 900, 900, null)],
        };

        var result = await world.RecordSale.HandleAsync(command);

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Empty(world.Sales.All);
        Assert.Equal(0, world.UnitOfWork.Commits);
        Assert.Equal(Money.Fcfa(10_000), session.ExpectedCash);
    }
}
