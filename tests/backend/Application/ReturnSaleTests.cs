using Soro.Application.Common;
using Soro.Application.Sales;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Sales;
using Soro.Domain.Stock;

namespace Soro.Application.Tests;

public class ReturnSaleTests
{
    /// <summary>Une vente de deux jus à 1 000 FCFA payée en espèces, dans une session ouverte, par la caissière.</summary>
    private static (TestWorld World, CashRegisterSession Session, Sale Sale, Product Product) Till(long discount = 0, params Payment[] payments)
    {
        var world = new TestWorld();
        var product = world.GivenProduct(stock: 10);
        var session = world.GivenOpenSession();
        var sale = session.RecordSale(new SaleDraft(
            SaleId.New(),
            1,
            TestWorld.Morning.AddMinutes(5),
            [SaleLine.Create(product.Id, product.Name, 2, Money.Fcfa(1_000))],
            Money.Fcfa(discount),
            payments.Length > 0 ? payments : [Payment.Cash(PaymentId.New(), Money.Fcfa(2_000 - discount), Money.Fcfa(2_000 - discount))],
            null));
        world.Sales.Put(sale);
        world.Sessions.Put(session);
        return (world, session, sale, product);
    }

    private static ReturnSaleCommand Return(Sale sale, int quantity = 1, bool resellable = true, Approval? approval = null, SaleReturnId? id = null) =>
        new(sale.Id, id ?? SaleReturnId.New(), [new ReturnRequest(0, quantity, resellable)], "Retour client", null, approval);

    [Fact]
    public async Task A_cashier_returns_one_article_with_the_approval_of_a_manager()
    {
        var (world, session, sale, product) = Till();
        var manager = world.GivenUser("gerant", pin: "4321", role: Role.Manager);

        var result = await world.ReturnSale.HandleAsync(Return(sale, approval: new Approval(manager.Id, "4321")));

        Assert.True(result.IsSuccess);
        Assert.Equal(1_000, result.Value!.TotalFcfa);
        Assert.Equal(1_000, result.Value.CashFcfa);
        Assert.Equal(11, product.QuantityOnHand);
        Assert.Equal(StockMovementKind.Return, Assert.Single(world.Movements.All).Kind);
        Assert.Equal(Money.Fcfa(1_000), session.CashRefunded);
        Assert.Equal(Money.Fcfa(11_000), session.ExpectedCash);
        var stored = Assert.Single(await world.Returns.ListForSaleAsync(sale.Id, world.Tenant, CancellationToken.None));
        Assert.Equal(manager.Id, stored.ApprovedBy);
        Assert.Equal(world.Cashier, stored.By);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.SaleReturned);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_manager_needs_nobody_else_to_validate()
    {
        var (world, _, sale, _) = Till();
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.ReturnSale.HandleAsync(Return(sale));

        Assert.True(result.IsSuccess);
        Assert.Null((await world.Returns.ListForSaleAsync(sale.Id, world.Tenant, CancellationToken.None)).Single().ApprovedBy);
    }

    [Fact]
    public async Task Without_approval_or_with_a_wrong_pin_nothing_happens_and_the_refusal_is_audited()
    {
        var (world, session, sale, product) = Till();
        var manager = world.GivenUser("gerant", pin: "4321", role: Role.Manager);

        var none = await world.ReturnSale.HandleAsync(Return(sale));
        var wrong = await world.ReturnSale.HandleAsync(Return(sale, approval: new Approval(manager.Id, "0000")));

        Assert.Equal(ErrorCodes.Forbidden, none.Error!.Code);
        Assert.Equal(ErrorCodes.Forbidden, wrong.Error!.Code);
        Assert.Empty(world.Returns.All);
        Assert.Equal(10, product.QuantityOnHand);
        Assert.Equal(Money.Zero, session.CashRefunded);
        Assert.Equal(2, world.Audit.Entries.Count(entry => entry.Action == AuditActions.ReturnRefused));
    }

    [Fact]
    public async Task A_colleague_who_is_not_a_manager_cannot_validate()
    {
        var (world, _, sale, _) = Till();
        var colleague = world.GivenUser("caissier2", pin: "1111", role: Role.Cashier);

        var result = await world.ReturnSale.HandleAsync(Return(sale, approval: new Approval(colleague.Id, "1111")));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task A_damaged_article_does_not_come_back_on_the_shelf_but_leaves_a_trace()
    {
        var (world, _, sale, product) = Till();
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.ReturnSale.HandleAsync(Return(sale, resellable: false));

        Assert.True(result.IsSuccess);
        Assert.Equal(10, product.QuantityOnHand);
        Assert.Equal([StockMovementKind.Return, StockMovementKind.Loss], world.Movements.All.Select(movement => movement.Kind).ToArray());
    }

    [Fact]
    public async Task Sending_the_same_return_twice_refunds_once()
    {
        var (world, session, sale, _) = Till();
        world.ActAs(world.Manager, Role.Manager);
        var command = Return(sale);

        var first = await world.ReturnSale.HandleAsync(command);
        var second = await world.ReturnSale.HandleAsync(command);

        Assert.False(first.Value!.AlreadyProcessed);
        Assert.True(second.Value!.AlreadyProcessed);
        Assert.Equal(1_000, second.Value.TotalFcfa);
        Assert.Equal(Money.Fcfa(1_000), session.CashRefunded);
        Assert.Single(world.Returns.All);
    }

    [Fact]
    public async Task Returning_more_than_what_was_sold_is_refused_and_nothing_changes()
    {
        var (world, session, sale, product) = Till();
        world.ActAs(world.Manager, Role.Manager);
        await world.ReturnSale.HandleAsync(Return(sale));

        var result = await world.ReturnSale.HandleAsync(Return(sale, quantity: 2));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Equal(11, product.QuantityOnHand);
        Assert.Equal(Money.Fcfa(1_000), session.CashRefunded);
    }

    [Fact]
    public async Task A_cash_refund_needs_an_open_session_and_changes_nothing_without_one()
    {
        var (world, session, sale, product) = Till();
        session.Close(Money.Fcfa(12_000), TestWorld.Morning.AddHours(8));
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.ReturnSale.HandleAsync(Return(sale));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("Ouvrez la caisse", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(10, product.QuantityOnHand);
        Assert.Empty(world.Returns.All);
    }

    [Fact]
    public async Task The_refund_goes_to_the_session_open_on_the_register_that_does_it_even_when_the_sale_was_in_another()
    {
        var (world, oldSession, sale, _) = Till();
        oldSession.Close(Money.Fcfa(12_000), TestWorld.Morning.AddHours(8));
        var today = world.GivenOpenSession();
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.ReturnSale.HandleAsync(Return(sale) with { Register = today.Assignment.Register });

        Assert.True(result.IsSuccess);
        Assert.Equal(Money.Fcfa(1_000), today.CashRefunded);
        Assert.Equal(Money.Zero, oldSession.CashRefunded);
    }

    [Fact]
    public async Task A_mobile_money_part_is_reported_to_the_manager_and_leaves_the_drawer_alone()
    {
        var (world, session, sale, _) = Till(payments: Payment.MobileMoney(PaymentId.New(), PaymentMethod.Wave, Money.Fcfa(2_000), "WV123456"));
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.ReturnSale.HandleAsync(Return(sale));

        Assert.Equal(0, result.Value!.CashFcfa);
        Assert.Equal([(PaymentMethod.Wave, 1_000L)], result.Value.Refund.Select(part => (part.Method, part.Amount.Amount)).ToArray());
        Assert.Equal(Money.Zero, session.CashRefunded);
    }

    [Fact]
    public async Task A_credit_part_wipes_the_debt_of_the_customer_first()
    {
        var world = new TestWorld();
        var product = world.GivenProduct(stock: 10);
        var session = world.GivenOpenSession();
        var customer = world.GivenCustomer(ceiling: 50_000, debt: 1_000);
        var sale = session.RecordSale(new SaleDraft(
            SaleId.New(), 1, TestWorld.Morning.AddMinutes(5), [SaleLine.Create(product.Id, product.Name, 2, Money.Fcfa(1_000))], Money.Zero,
            [Payment.Credit(PaymentId.New(), Money.Fcfa(1_000)), Payment.Cash(PaymentId.New(), Money.Fcfa(1_000), Money.Fcfa(1_000))], customer.Id));
        world.Sales.Put(sale);
        world.Sessions.Put(session);
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.ReturnSale.HandleAsync(Return(sale));

        Assert.Equal(1_000, result.Value!.CreditFcfa);
        Assert.Equal(0, result.Value.CashFcfa);
        Assert.Equal(Money.Zero, customer.Debt);
    }

    [Fact]
    public async Task A_sale_of_another_shop_is_not_found()
    {
        var (world, _, sale, _) = Till();
        world.ActAs(world.Manager, Role.Manager);
        var elsewhere = new TestWorld();
        elsewhere.ActAs(elsewhere.Manager, Role.Manager);

        var result = await elsewhere.ReturnSale.HandleAsync(Return(sale));

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task A_return_needs_a_reason()
    {
        var (world, _, sale, _) = Till();
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.ReturnSale.HandleAsync(Return(sale) with { Reason = " " });

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
    }

    [Fact]
    public async Task Cancelling_with_the_approval_of_a_manager_puts_everything_back_and_refunds_the_cash()
    {
        var (world, session, sale, product) = Till();
        var manager = world.GivenUser("gerant", pin: "4321", role: Role.Manager);

        var result = await world.CancelSale.HandleAsync(new CancelSaleCommand(sale.Id, "Erreur de saisie", Approval: new Approval(manager.Id, "4321")));

        Assert.True(result.IsSuccess);
        Assert.Equal(2_000, result.Value!.TotalFcfa);
        Assert.Equal(12, product.QuantityOnHand);
        Assert.Equal(StockMovementKind.SaleCancellation, Assert.Single(world.Movements.All).Kind);
        Assert.Equal(Money.Fcfa(2_000), session.CashRefunded);
        Assert.Equal(ReturnKind.Cancellation, Assert.Single(world.Returns.All).Kind);
        Assert.Equal(SaleStatus.Cancelled, (await world.Sales.GetAsync(sale.Id, world.Tenant, CancellationToken.None))!.Status);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.SaleCancelled);
    }

    [Fact]
    public async Task A_sale_that_already_has_a_return_cannot_be_cancelled_whole()
    {
        var (world, _, sale, _) = Till();
        world.ActAs(world.Manager, Role.Manager);
        await world.ReturnSale.HandleAsync(Return(sale));

        var result = await world.CancelSale.HandleAsync(new CancelSaleCommand(sale.Id, "Erreur"));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("déjà un retour", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancelled_sale_cannot_be_returned()
    {
        var (world, _, sale, _) = Till();
        world.ActAs(world.Manager, Role.Manager);
        await world.CancelSale.HandleAsync(new CancelSaleCommand(sale.Id, "Erreur"));

        var result = await world.ReturnSale.HandleAsync(Return(sale));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
    }
}
