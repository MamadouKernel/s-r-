using Soro.Application.Common;
using Soro.Application.Customers;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Sales;

namespace Soro.Application.Tests;

/// <summary>Correctifs de l'audit du 25/10/2026 (docs/decisions/0018) : prix, approbation par PIN, dette fantôme, références, sessions.</summary>
public class PriceBelowCatalogueTests
{
    private static RecordSaleCommand SaleAt(TestWorld world, Domain.Cash.CashRegisterSession session, Domain.Stock.Product product, long price, bool offline = false) =>
        world.CashSale(session, price) with
        {
            Lines = [new SaleLineInput(product.Id, product.Name, 1, price)],
            FromOfflineQueue = offline,
        };

    [Fact]
    public async Task A_cashier_cannot_sell_online_below_the_catalogue_price()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var product = world.GivenProduct(name: "Jus d'orange 1L", barcode: null, stock: 10);

        var result = await world.RecordSale.HandleAsync(SaleAt(world, session, product, price: 100));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Contains("prix inférieur", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(world.Sales.All);
        Assert.Equal(10, product.QuantityOnHand);
    }

    [Fact]
    public async Task A_manager_may_sell_below_the_catalogue_and_it_is_in_the_journal()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var product = world.GivenProduct(name: "Jus d'orange 1L", barcode: null);
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.RecordSale.HandleAsync(SaleAt(world, session, product, price: 100));

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(world.Audit.Entries, item => item.Action == AuditActions.SalePriceLowered);
        Assert.Contains("Jus d'orange 1L", entry.Detail, StringComparison.Ordinal);
        Assert.Contains("100", entry.Detail, StringComparison.Ordinal);
        Assert.Contains("600", entry.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sale_from_the_offline_queue_is_recorded_because_it_took_place_and_flagged_for_the_owner()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var product = world.GivenProduct(name: "Jus d'orange 1L", barcode: null);

        var result = await world.RecordSale.HandleAsync(SaleAt(world, session, product, price: 100, offline: true));

        Assert.True(result.IsSuccess);
        Assert.Single(world.Sales.All);
        var entry = Assert.Single(world.Audit.Entries, item => item.Action == AuditActions.SalePriceLowered);
        Assert.Equal(world.Cashier, entry.User);
    }

    [Fact]
    public async Task The_sync_handler_marks_its_sales_as_coming_from_the_queue()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var product = world.GivenProduct(name: "Jus", barcode: null);

        var result = await world.SyncSales.HandleAsync(new SyncSalesCommand([SaleAt(world, session, product, price: 100)]));

        Assert.Equal(SyncOutcome.Accepted, Assert.Single(result.Statuses).Outcome);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.SalePriceLowered);
    }

    [Theory]
    [InlineData(600)]
    [InlineData(750)]
    public async Task A_price_equal_or_above_the_catalogue_says_nothing(long price)
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var product = world.GivenProduct(barcode: null);

        var result = await world.RecordSale.HandleAsync(SaleAt(world, session, product, price));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(world.Audit.Entries, entry => entry.Action == AuditActions.SalePriceLowered);
    }

    [Fact]
    public async Task A_product_unknown_to_the_commerce_has_no_price_to_compare()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();

        var result = await world.RecordSale.HandleAsync(world.CashSale(session, price: 100));

        Assert.True(result.IsSuccess);
        Assert.Empty(world.Audit.Entries);
    }

    [Fact]
    public async Task Many_lowered_lines_make_one_journal_entry_that_counts_the_rest()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var items = Enumerable.Range(1, 5).Select(index => world.GivenProduct(name: $"Article {index}", barcode: null)).ToArray();
        world.ActAs(world.Manager, Role.Manager);
        var command = world.CashSale(session, 500) with
        {
            Lines = items.Select(item => new SaleLineInput(item.Id, item.Name, 1, 100)).ToArray(),
            Payments = [new PaymentInput(PaymentId.New(), PaymentMethod.Cash, 500, 500, null)],
        };

        await world.RecordSale.HandleAsync(command);

        var entry = Assert.Single(world.Audit.Entries, item => item.Action == AuditActions.SalePriceLowered);
        Assert.Contains("+2 autres", entry.Detail, StringComparison.Ordinal);
    }
}

public class ApprovalByPinTests
{
    private static (TestWorld World, Domain.Cash.CashRegisterSession Session, User Manager) Till()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var manager = world.GivenUser("gerant", pin: "4321", role: Role.Manager);
        return (world, session, manager);
    }

    [Fact]
    public async Task The_right_pin_of_a_manager_approves_a_large_discount()
    {
        var (world, session, manager) = Till();

        var result = await world.RecordSale.HandleAsync(world.CashSale(session, 1_000, discount: 300, approval: new Approval(manager.Id, "4321")));

        Assert.True(result.IsSuccess);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.DiscountApproved);
    }

    [Theory]
    [InlineData("0000")]
    [InlineData("")]
    public async Task A_wrong_or_missing_pin_does_not_approve_even_with_the_right_manager_id(string pin)
    {
        var (world, session, manager) = Till();

        var result = await world.RecordSale.HandleAsync(world.CashSale(session, 1_000, discount: 300, approval: new Approval(manager.Id, pin)));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Empty(world.Sales.All);
    }

    [Fact]
    public async Task The_manager_id_alone_proves_nothing()
    {
        var (world, session, manager) = Till();

        var result = await world.RecordSale.HandleAsync(world.CashSale(session, 1_000, discount: 300));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.NotNull(manager);
    }

    [Fact]
    public async Task Wrong_pins_are_counted_and_lock_the_manager_account_like_at_the_login()
    {
        var (world, session, manager) = Till();
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            await world.RecordSale.HandleAsync(world.CashSale(session, 1_000, discount: 300, approval: new Approval(manager.Id, "0000")));
        }

        Assert.True(manager.IsLocked(world.Clock.UtcNow));
        var result = await world.RecordSale.HandleAsync(world.CashSale(session, 1_000, discount: 300, approval: new Approval(manager.Id, "4321")));
        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task The_owner_has_no_pin_and_a_disabled_manager_cannot_approve()
    {
        var (world, session, manager) = Till();
        var owner = User.RegisterOwner(UserId.New(), world.Tenant, EmailAddress.Parse("mamadou@exemple.ci"), "Mamadou", "password-hash", world.Clock.UtcNow);
        world.Users.Put(owner);
        manager.Disable(world.Clock.UtcNow);

        var byOwner = await world.RecordSale.HandleAsync(world.CashSale(session, 1_000, discount: 300, approval: new Approval(owner.Id, "")));
        var byDisabled = await world.RecordSale.HandleAsync(world.CashSale(session, 1_000, discount: 300, approval: new Approval(manager.Id, "4321")));

        Assert.Equal(ErrorCodes.Forbidden, byOwner.Error!.Code);
        Assert.Equal(ErrorCodes.Forbidden, byDisabled.Error!.Code);
    }

    [Fact]
    public async Task A_manager_ringing_the_sale_himself_needs_no_pin()
    {
        var (world, session, manager) = Till();
        world.ActAs(manager.Id, Role.Manager);

        var result = await world.RecordSale.HandleAsync(world.CashSale(session, 1_000, discount: 300));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task The_credit_ceiling_needs_the_pin_too()
    {
        var (world, session, manager) = Till();
        var customer = world.GivenCustomer(ceiling: 1_000);
        var command = world.CashSale(session, 5_000) with
        {
            Customer = customer.Id,
            Payments = [new PaymentInput(PaymentId.New(), PaymentMethod.Credit, 5_000, null, null)],
        };

        var withoutPin = await world.RecordSale.HandleAsync(command with { CreditApproval = new Approval(manager.Id, "0000") });
        var withPin = await world.RecordSale.HandleAsync(command with { CreditApproval = new Approval(manager.Id, "4321") });

        Assert.Equal(ErrorCodes.CreditLimitExceeded, withoutPin.Error!.Code);
        Assert.True(withPin.IsSuccess);
        Assert.Equal(Money.Fcfa(5_000), customer.Debt);
    }
}

public class RejectedSaleLeavesNothingTests
{
    [Fact]
    public async Task A_credit_sale_the_session_refuses_adds_no_debt()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var customer = world.GivenCustomer(ceiling: 20_000, debt: 1_000);
        var command = world.CashSale(session, 5_000) with
        {
            Customer = customer.Id,
            At = session.OpenedAt.AddMinutes(-30), // l'horloge de la tablette était en retard : la session n'accepte pas cette vente
            Payments = [new PaymentInput(PaymentId.New(), PaymentMethod.Credit, 5_000, null, null)],
        };

        var result = await world.RecordSale.HandleAsync(command);

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Equal(Money.Fcfa(1_000), customer.Debt);
        Assert.Empty(world.Sales.All);
    }

    [Fact]
    public async Task A_refused_sale_leaves_no_approval_or_overrun_entry_in_the_journal()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var customer = world.GivenCustomer(ceiling: 1_000);
        var command = world.CashSale(session, 5_000) with
        {
            Customer = customer.Id,
            At = session.OpenedAt.AddMinutes(-30),
            EnforceCreditCeiling = false,
            FromOfflineQueue = true,
            Payments = [new PaymentInput(PaymentId.New(), PaymentMethod.Credit, 5_000, null, null)],
        };

        await world.RecordSale.HandleAsync(command);

        Assert.DoesNotContain(world.Audit.Entries, entry => entry.Action == AuditActions.CreditExceeded);
    }

    [Fact]
    public async Task A_rejected_credit_sale_in_a_batch_does_not_raise_the_debt_through_the_next_sale()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var customer = world.GivenCustomer(ceiling: 20_000, debt: 0);
        var bad = world.CashSale(session, 5_000, sequence: 1) with
        {
            Customer = customer.Id,
            At = session.OpenedAt.AddMinutes(-30),
            Payments = [new PaymentInput(PaymentId.New(), PaymentMethod.Credit, 5_000, null, null)],
        };
        var good = world.CashSale(session, 700, sequence: 2);

        var result = await world.SyncSales.HandleAsync(new SyncSalesCommand([bad, good]));

        Assert.Equal([SyncOutcome.Rejected, SyncOutcome.Accepted], result.Statuses.Select(status => status.Outcome));
        Assert.Equal(Money.Zero, customer.Debt);
    }
}

public class MobileMoneyReferenceAcrossRepaymentsTests
{
    [Fact]
    public async Task A_sale_refuses_a_reference_already_used_by_a_repayment()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var customer = world.GivenCustomer(debt: 5_000);
        world.ActAs(world.Manager, Role.Manager);
        var repayment = await world.RecordRepayment.HandleAsync(
            new RecordRepaymentCommand(DebtRepaymentId.New(), customer.Id, 1_000, PaymentMethod.OrangeMoney, "ABC123"));
        Assert.True(repayment.IsSuccess);

        var result = await world.RecordSale.HandleAsync(world.MobileSale(session, "ABC123"));

        Assert.Equal(ErrorCodes.MobileMoneyReferenceUsed, result.Error!.Code);
        Assert.Empty(world.Sales.All);
    }
}

public class PasswordChangeEndsSessionsTests
{
    [Fact]
    public async Task Resetting_the_owner_password_ends_the_sessions_opened_before()
    {
        var world = new TestWorld();
        var owner = User.RegisterOwner(UserId.New(), world.Tenant, EmailAddress.Parse("mamadou@exemple.ci"), "Mamadou", "old-hash", world.Clock.UtcNow.AddDays(-1));
        world.Users.Put(owner);
        var tokenIssuedAt = world.Clock.UtcNow;
        world.Clock.UtcNow = tokenIssuedAt.AddMinutes(30);

        owner.ChangePassword("new-hash", world.Clock.UtcNow);

        Assert.False(await world.CheckSession.HandleAsync(world.Tenant, owner.Id, tokenIssuedAt));
        Assert.True(await world.CheckSession.HandleAsync(world.Tenant, owner.Id, world.Clock.UtcNow.AddSeconds(5)));
    }
}
