using Soro.Application.Common;
using Soro.Application.Customers;
using Soro.Application.Sales;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Customers;
using Soro.Domain.Identity;
using Soro.Domain.Sales;

namespace Soro.Application.Tests;

public class CustomerManagementTests
{
    private static TestWorld As(Role role)
    {
        var world = new TestWorld();
        world.ActAs(role == Role.Manager ? world.Manager : role == Role.Owner ? world.Owner : world.Cashier, role);
        return world;
    }

    [Fact]
    public async Task A_cashier_creates_a_customer_at_the_till_and_gets_the_default_ceiling_SC_C1()
    {
        var world = As(Role.Cashier);

        var result = await world.CreateCustomer.HandleAsync(new CreateCustomerCommand(CustomerId.New(), "  Awa Koné ", "07 07 07 07 07", null));

        Assert.True(result.IsSuccess);
        var view = result.Value!.Customer;
        Assert.Equal("Awa Koné", view.Name);
        Assert.Equal("+2250707070707", view.Phone);
        Assert.Equal(20_000, view.CreditCeilingFcfa);
        Assert.Equal(0, view.DebtFcfa);
        Assert.Equal(20_000, view.AvailableCreditFcfa);
    }

    [Fact]
    public async Task A_manager_chooses_the_ceiling_but_a_cashier_may_not_SC_C10()
    {
        var manager = As(Role.Manager);
        var chosen = await manager.CreateCustomer.HandleAsync(new CreateCustomerCommand(CustomerId.New(), "Issa Diallo", null, 50_000));
        Assert.Equal(50_000, chosen.Value!.Customer.CreditCeilingFcfa);

        var cashier = As(Role.Cashier);
        var refused = await cashier.CreateCustomer.HandleAsync(new CreateCustomerCommand(CustomerId.New(), "Issa Diallo", null, 500_000));
        Assert.Equal(ErrorCodes.Forbidden, refused.Error!.Code);
        Assert.Empty(cashier.Customers.All);
    }

    [Theory]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Stock_keepers_and_accountants_do_not_create_customers(Role role)
    {
        var world = new TestWorld();
        world.ActAs(UserId.New(), role);

        var result = await world.CreateCustomer.HandleAsync(new CreateCustomerCommand(CustomerId.New(), "Awa Koné", null, null));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task The_same_phone_in_another_spelling_is_refused_and_a_replay_by_id_is_acknowledged()
    {
        var world = As(Role.Cashier);
        var id = CustomerId.New();
        await world.CreateCustomer.HandleAsync(new CreateCustomerCommand(id, "Awa Koné", "0707070707", null));

        var other = await world.CreateCustomer.HandleAsync(new CreateCustomerCommand(CustomerId.New(), "Autre Awa", "+225 07 07 07 07 07", null));
        var replay = await world.CreateCustomer.HandleAsync(new CreateCustomerCommand(id, "Awa Koné", "0707070707", null));

        Assert.Equal(ErrorCodes.PhoneTaken, other.Error!.Code);
        Assert.DoesNotContain("0707070707", other.Error.Message); // le numéro est masqué
        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Single(world.Customers.All);
    }

    [Fact]
    public async Task A_missing_phone_is_fine_a_bad_phone_or_name_is_refused()
    {
        var world = As(Role.Cashier);

        Assert.True((await world.CreateCustomer.HandleAsync(new CreateCustomerCommand(CustomerId.New(), "Sans téléphone", null, null))).IsSuccess);
        Assert.Equal(ErrorCodes.RuleViolation, (await world.CreateCustomer.HandleAsync(new CreateCustomerCommand(CustomerId.New(), "Awa", "123", null))).Error!.Code);
        Assert.Equal(ErrorCodes.RuleViolation, (await world.CreateCustomer.HandleAsync(new CreateCustomerCommand(CustomerId.New(), "A", null, null))).Error!.Code);
    }

    [Fact]
    public async Task A_manager_edits_name_phone_and_ceiling_and_a_cashier_cannot()
    {
        var world = As(Role.Manager);
        var customer = world.GivenCustomer(debt: 8_000);

        var edited = await world.UpdateCustomer.HandleAsync(new UpdateCustomerCommand(customer.Id, "Awa Koné-Traoré", "01 02 03 04 05", 50_000));

        Assert.Equal(50_000, edited.Value!.CreditCeilingFcfa);
        Assert.Equal(8_000, edited.Value.DebtFcfa);
        Assert.Equal("+2250102030405", edited.Value.Phone);

        world.ActAs(world.Cashier, Role.Cashier);
        var refused = await world.UpdateCustomer.HandleAsync(new UpdateCustomerCommand(customer.Id, "X Y", null, 1));
        Assert.Equal(ErrorCodes.Forbidden, refused.Error!.Code);
    }

    [Fact]
    public async Task Editing_keeps_the_own_phone_but_refuses_the_phone_of_another_customer()
    {
        var world = As(Role.Manager);
        var awa = world.GivenCustomer("Awa Koné", "0707070707");
        world.GivenCustomer("Issa Diallo", "0102030405");

        Assert.True((await world.UpdateCustomer.HandleAsync(new UpdateCustomerCommand(awa.Id, "Awa K.", "0707070707", 30_000))).IsSuccess);
        var clash = await world.UpdateCustomer.HandleAsync(new UpdateCustomerCommand(awa.Id, "Awa K.", "01 02 03 04 05", 30_000));

        Assert.Equal(ErrorCodes.PhoneTaken, clash.Error!.Code);
    }

    [Fact]
    public async Task An_unknown_customer_or_one_of_another_commerce_is_not_found_SC_C10()
    {
        var world = As(Role.Manager);
        var foreign = world.GivenCustomer(tenant: TenantId.New());

        Assert.Equal(ErrorCodes.NotFound, (await world.UpdateCustomer.HandleAsync(new UpdateCustomerCommand(CustomerId.New(), "Awa Koné", null, 1))).Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, (await world.UpdateCustomer.HandleAsync(new UpdateCustomerCommand(foreign.Id, "Awa Koné", null, 1))).Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, (await world.DeactivateCustomer.HandleAsync(new DeactivateCustomerCommand(foreign.Id))).Error!.Code);
    }

    [Fact]
    public async Task A_customer_with_a_debt_cannot_be_deactivated_and_one_without_disappears_from_the_lists_SC_C9()
    {
        var world = As(Role.Manager);
        var indebted = world.GivenCustomer("Awa Koné", "0707070707", debt: 500);
        var clean = world.GivenCustomer("Issa Diallo", "0102030405");

        Assert.Equal(ErrorCodes.RuleViolation, (await world.DeactivateCustomer.HandleAsync(new DeactivateCustomerCommand(indebted.Id))).Error!.Code);
        Assert.True((await world.DeactivateCustomer.HandleAsync(new DeactivateCustomerCommand(clean.Id))).IsSuccess);

        var visible = (await world.ListCustomers.HandleAsync(new ListCustomersQuery(null, false))).Value!;
        Assert.Equal(["Awa Koné"], visible.Select(view => view.Name));
        var all = (await world.ListCustomers.HandleAsync(new ListCustomersQuery(null, true))).Value!;
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task A_cashier_cannot_deactivate()
    {
        var world = As(Role.Cashier);
        var customer = world.GivenCustomer();

        Assert.Equal(ErrorCodes.Forbidden, (await world.DeactivateCustomer.HandleAsync(new DeactivateCustomerCommand(customer.Id))).Error!.Code);
    }

    [Fact]
    public async Task The_list_searches_by_name_or_by_digits_of_the_phone_and_shows_debt_and_available_credit()
    {
        var world = As(Role.Cashier);
        world.GivenCustomer("Awa Koné", "07 07 07 07 07", 20_000, 8_000);
        world.GivenCustomer("Issa Diallo", "01 02 03 04 05");
        world.GivenCustomer("Marie Awa", null);

        var byName = (await world.ListCustomers.HandleAsync(new ListCustomersQuery("awa", false))).Value!;
        var byPhone = (await world.ListCustomers.HandleAsync(new ListCustomersQuery("0102", false))).Value!;

        Assert.Equal(["Awa Koné", "Marie Awa"], byName.Select(view => view.Name));
        Assert.Equal(["Issa Diallo"], byPhone.Select(view => view.Name));
        var awa = byName[0];
        Assert.Equal(8_000, awa.DebtFcfa);
        Assert.Equal(12_000, awa.AvailableCreditFcfa);
    }

    [Fact]
    public async Task The_cashier_never_sees_deactivated_customers_even_when_asking_for_them()
    {
        var world = As(Role.Cashier);
        var customer = world.GivenCustomer();
        customer.Deactivate();

        var result = await world.ListCustomers.HandleAsync(new ListCustomersQuery(null, true));

        Assert.Empty(result.Value!);
    }

    [Theory]
    [InlineData(Role.Owner, true)]
    [InlineData(Role.Manager, true)]
    [InlineData(Role.Cashier, true)]
    [InlineData(Role.Accountant, true)]
    [InlineData(Role.StockKeeper, false)]
    public async Task Who_may_read_the_customers(Role role, bool allowed)
    {
        var world = new TestWorld();
        world.ActAs(UserId.New(), role);
        world.GivenCustomer();

        var result = await world.ListCustomers.HandleAsync(new ListCustomersQuery(null, false));

        Assert.Equal(allowed, result.IsSuccess);
    }

    [Fact]
    public async Task Another_commerce_sees_none_of_the_customers_SC_C10()
    {
        var world = As(Role.Manager);
        world.GivenCustomer(tenant: TenantId.New());

        Assert.Empty((await world.ListCustomers.HandleAsync(new ListCustomersQuery(null, true))).Value!);
    }
}

public class RepaymentTests
{
    private static TestWorld WithDebt(long debt = 8_000, Role role = Role.Cashier)
    {
        var world = new TestWorld();
        world.ActAs(role == Role.Manager ? world.Manager : world.Cashier, role);
        return world;
    }

    private static RecordRepaymentCommand Cash(Customer customer, long amount, DebtRepaymentId? id = null) =>
        new(id ?? DebtRepaymentId.New(), customer.Id, amount, PaymentMethod.Cash, null);

    [Fact]
    public async Task A_partial_repayment_lowers_the_debt_and_is_audited_SC_C5()
    {
        var world = WithDebt();
        var customer = world.GivenCustomer(debt: 8_000);

        var result = await world.RecordRepayment.HandleAsync(Cash(customer, 5_000));

        Assert.True(result.IsSuccess);
        Assert.Equal(3_000, result.Value!.DebtFcfa);
        Assert.Equal(Money.Fcfa(3_000), customer.Debt);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.DebtRepaid, entry.Action);
        Assert.Equal(world.Cashier, entry.User);
        Assert.Equal(customer.Id.Value, entry.ResourceId);
        Assert.Contains("5 000 FCFA", entry.Detail);
        Assert.Contains("espèces", entry.Detail);
    }

    [Fact]
    public async Task A_repayment_over_the_debt_is_refused_and_changes_nothing_SC_C5()
    {
        var world = WithDebt();
        var customer = world.GivenCustomer(debt: 8_000);

        var result = await world.RecordRepayment.HandleAsync(Cash(customer, 8_001));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("dépasse la dette", result.Error.Message);
        Assert.Equal(Money.Fcfa(8_000), customer.Debt);
        Assert.Empty(world.Repayments.All);
        Assert.Empty(world.Audit.Entries);
    }

    [Fact]
    public async Task Replaying_a_repayment_does_not_count_it_twice_SC_C5()
    {
        var world = WithDebt();
        var customer = world.GivenCustomer(debt: 8_000);
        var id = DebtRepaymentId.New();

        await world.RecordRepayment.HandleAsync(Cash(customer, 5_000, id));
        var replay = await world.RecordRepayment.HandleAsync(Cash(customer, 5_000, id));

        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Equal(3_000, replay.Value.DebtFcfa);
        Assert.Single(world.Repayments.All);
    }

    [Fact]
    public async Task Mobile_money_needs_a_reference_and_a_reference_serves_once_SC_C6()
    {
        var world = WithDebt();
        var customer = world.GivenCustomer(debt: 8_000);

        var missing = await world.RecordRepayment.HandleAsync(new(DebtRepaymentId.New(), customer.Id, 1_000, PaymentMethod.Wave, null));
        var first = await world.RecordRepayment.HandleAsync(new(DebtRepaymentId.New(), customer.Id, 1_000, PaymentMethod.Wave, "WV-1"));
        var again = await world.RecordRepayment.HandleAsync(new(DebtRepaymentId.New(), customer.Id, 1_000, PaymentMethod.Wave, "wv-1"));
        var otherOperator = await world.RecordRepayment.HandleAsync(new(DebtRepaymentId.New(), customer.Id, 1_000, PaymentMethod.OrangeMoney, "WV-1"));

        Assert.Equal(ErrorCodes.RuleViolation, missing.Error!.Code);
        Assert.True(first.IsSuccess);
        Assert.Equal(ErrorCodes.MobileMoneyReferenceUsed, again.Error!.Code);
        Assert.True(otherOperator.IsSuccess); // la référence est propre à un opérateur
        Assert.Equal(Money.Fcfa(6_000), customer.Debt);
    }

    [Fact]
    public async Task A_reference_already_used_by_a_sale_is_refused_SC_C6()
    {
        var world = WithDebt();
        var customer = world.GivenCustomer(debt: 8_000);
        var session = world.GivenOpenSession();
        await world.RecordSale.HandleAsync(world.CashSale(session) with
        {
            Payments = [new PaymentInput(PaymentId.New(), PaymentMethod.OrangeMoney, 1_000, null, "OM-9")],
        });

        var result = await world.RecordRepayment.HandleAsync(new(DebtRepaymentId.New(), customer.Id, 1_000, PaymentMethod.OrangeMoney, "OM-9"));

        Assert.Equal(ErrorCodes.MobileMoneyReferenceUsed, result.Error!.Code);
        Assert.Equal(Money.Fcfa(8_000), customer.Debt);
    }

    [Fact]
    public async Task Credit_is_not_a_way_to_repay_and_zero_is_refused()
    {
        var world = WithDebt();
        var customer = world.GivenCustomer(debt: 8_000);

        Assert.Equal(ErrorCodes.RuleViolation, (await world.RecordRepayment.HandleAsync(new(DebtRepaymentId.New(), customer.Id, 1_000, PaymentMethod.Credit, null))).Error!.Code);
        Assert.Equal(ErrorCodes.RuleViolation, (await world.RecordRepayment.HandleAsync(Cash(customer, 0))).Error!.Code);
    }

    [Theory]
    [InlineData(Role.Accountant)]
    [InlineData(Role.StockKeeper)]
    public async Task Only_the_till_profiles_collect_repayments(Role role)
    {
        var world = new TestWorld();
        world.ActAs(UserId.New(), role);
        var customer = world.GivenCustomer(debt: 8_000);

        var result = await world.RecordRepayment.HandleAsync(Cash(customer, 1_000));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task A_customer_of_another_commerce_is_not_found()
    {
        var world = WithDebt();
        var foreign = world.GivenCustomer(debt: 8_000, tenant: TenantId.New());

        Assert.Equal(ErrorCodes.NotFound, (await world.RecordRepayment.HandleAsync(Cash(foreign, 1_000))).Error!.Code);
        Assert.Equal(Money.Fcfa(8_000), foreign.Debt);
    }

    [Fact]
    public async Task The_history_lists_newest_first_to_managers_and_accountants_but_not_to_cashiers()
    {
        var world = WithDebt();
        var customer = world.GivenCustomer(debt: 8_000);
        await world.RecordRepayment.HandleAsync(Cash(customer, 1_000));
        world.Clock.UtcNow = world.Clock.UtcNow.AddHours(1);
        await world.RecordRepayment.HandleAsync(Cash(customer, 2_000));

        Assert.Equal(ErrorCodes.Forbidden, (await world.ListRepayments.HandleAsync(new ListRepaymentsQuery(customer.Id, 50))).Error!.Code);

        world.ActAs(world.Manager, Role.Manager);
        var history = (await world.ListRepayments.HandleAsync(new ListRepaymentsQuery(customer.Id, 50))).Value!;
        Assert.Equal([2_000L, 1_000L], history.Select(item => item.AmountFcfa));

        world.ActAs(UserId.New(), Role.Accountant);
        Assert.True((await world.ListRepayments.HandleAsync(new ListRepaymentsQuery(customer.Id, 50))).IsSuccess);
        Assert.Equal(ErrorCodes.NotFound, (await world.ListRepayments.HandleAsync(new ListRepaymentsQuery(CustomerId.New(), 50))).Error!.Code);
    }
}

public class CreditSaleTests
{
    private static RecordSaleCommand CreditSale(
        TestWorld world,
        CashRegisterSession session,
        CustomerId? customer,
        long credit,
        long cash = 0,
        UserId? approver = null,
        bool enforce = true,
        long sequence = 1)
    {
        var payments = new List<PaymentInput> { new(PaymentId.New(), PaymentMethod.Credit, credit, null, null) };
        if (cash > 0)
        {
            payments.Add(new(PaymentId.New(), PaymentMethod.Cash, cash, cash, null));
        }

        return world.CashSale(session, credit + cash, sequence) with
        {
            Customer = customer, Payments = payments, CreditApprover = approver, EnforceCreditCeiling = enforce,
        };
    }

    private static (TestWorld World, CashRegisterSession Session) Till()
    {
        var world = new TestWorld();
        return (world, world.GivenOpenSession());
    }

    [Fact]
    public async Task A_credit_sale_within_the_ceiling_adds_to_the_debt_SC_C2()
    {
        var (world, session) = Till();
        var customer = world.GivenCustomer(ceiling: 20_000);

        var result = await world.RecordSale.HandleAsync(CreditSale(world, session, customer.Id, 8_000));

        Assert.True(result.IsSuccess);
        Assert.Equal(Money.Fcfa(8_000), customer.Debt);
        Assert.Equal(Money.Fcfa(12_000), customer.AvailableCredit);
    }

    [Fact]
    public async Task Only_the_credit_part_of_a_mixed_payment_becomes_debt()
    {
        var (world, session) = Till();
        var customer = world.GivenCustomer();

        await world.RecordSale.HandleAsync(CreditSale(world, session, customer.Id, credit: 3_000, cash: 5_000));

        Assert.Equal(Money.Fcfa(3_000), customer.Debt);
    }

    [Fact]
    public async Task A_cash_sale_with_a_customer_does_not_touch_the_debt()
    {
        var (world, session) = Till();
        var customer = world.GivenCustomer(debt: 1_000);

        await world.RecordSale.HandleAsync(world.CashSale(session) with { Customer = customer.Id });

        Assert.Equal(Money.Fcfa(1_000), customer.Debt);
    }

    [Fact]
    public async Task Above_the_ceiling_the_sale_is_refused_with_the_figures_and_the_refusal_is_traced_SC_C3()
    {
        var (world, session) = Till();
        var customer = world.GivenCustomer(ceiling: 20_000, debt: 8_000);

        var result = await world.RecordSale.HandleAsync(CreditSale(world, session, customer.Id, 15_000));

        Assert.Equal(ErrorCodes.CreditLimitExceeded, result.Error!.Code);
        Assert.Contains("il reste 12 000 FCFA", result.Error.Message);
        Assert.Contains("validation d'un gérant", result.Error.Message);
        Assert.Equal(Money.Fcfa(8_000), customer.Debt);
        Assert.Empty(world.Sales.All);
        Assert.Equal(AuditActions.CreditRefused, Assert.Single(world.Audit.Entries).Action);
    }

    [Fact]
    public async Task A_manager_named_as_approver_lets_it_through_and_it_is_traced_SC_C3()
    {
        var (world, session) = Till();
        var customer = world.GivenCustomer(ceiling: 20_000, debt: 8_000);

        var result = await world.RecordSale.HandleAsync(CreditSale(world, session, customer.Id, 15_000, approver: world.Manager));

        Assert.True(result.IsSuccess);
        Assert.Equal(Money.Fcfa(23_000), customer.Debt);
        Assert.Equal(AuditActions.CreditApproved, Assert.Single(world.Audit.Entries).Action);
    }

    [Fact]
    public async Task A_manager_ringing_the_sale_himself_approves_it()
    {
        var (world, session) = Till();
        var customer = world.GivenCustomer(ceiling: 1_000);
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.RecordSale.HandleAsync(CreditSale(world, session, customer.Id, 5_000));

        Assert.True(result.IsSuccess);
        Assert.Equal(AuditActions.CreditApproved, Assert.Single(world.Audit.Entries).Action);
    }

    [Fact]
    public async Task An_approver_who_is_not_a_manager_does_not_count()
    {
        var (world, session) = Till();
        var customer = world.GivenCustomer(ceiling: 1_000);
        var colleague = UserId.New();
        world.Directory.Set(world.Tenant, colleague, Role.Cashier);

        var result = await world.RecordSale.HandleAsync(CreditSale(world, session, customer.Id, 5_000, approver: colleague));

        Assert.Equal(ErrorCodes.CreditLimitExceeded, result.Error!.Code);
        Assert.Equal(Money.Zero, customer.Debt);
    }

    [Fact]
    public async Task A_sale_from_the_sync_that_exceeds_the_ceiling_is_recorded_and_flagged_SC_C4()
    {
        var (world, session) = Till();
        var customer = world.GivenCustomer(ceiling: 20_000, debt: 15_000);

        var result = await world.SyncSales.HandleAsync(new SyncSalesCommand([CreditSale(world, session, customer.Id, 10_000)]));

        Assert.Equal(SyncOutcome.Accepted, Assert.Single(result.Statuses).Outcome);
        Assert.Equal(Money.Fcfa(25_000), customer.Debt);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CreditExceeded, entry.Action);
        Assert.Contains("Awa Koné", entry.Detail);
        Assert.Contains("plafond 20 000 FCFA", entry.Detail);
    }

    [Fact]
    public async Task The_sync_still_rejects_a_sale_for_an_unknown_customer_with_a_clear_message()
    {
        var (world, session) = Till();

        var result = await world.SyncSales.HandleAsync(new SyncSalesCommand([CreditSale(world, session, CustomerId.New(), 1_000)]));

        var status = Assert.Single(result.Statuses);
        Assert.Equal(SyncOutcome.Rejected, status.Outcome);
        Assert.Equal(ErrorCodes.NotFound, status.ErrorCode);
        Assert.Contains("Client introuvable", status.Message);
    }

    [Fact]
    public async Task A_credit_sale_needs_a_customer_of_this_commerce_and_an_active_one()
    {
        var (world, session) = Till();
        var foreign = world.GivenCustomer(tenant: TenantId.New());
        var inactive = world.GivenCustomer("Inactif", "0102030405");
        inactive.Deactivate();

        Assert.Equal(ErrorCodes.NotFound, (await world.RecordSale.HandleAsync(CreditSale(world, session, foreign.Id, 1_000))).Error!.Code);
        var refused = await world.RecordSale.HandleAsync(CreditSale(world, session, inactive.Id, 1_000, sequence: 2));
        Assert.Equal(ErrorCodes.RuleViolation, refused.Error!.Code);
        Assert.Contains("désactivé", refused.Error.Message);
        Assert.Equal(Money.Zero, foreign.Debt);
    }

    [Fact]
    public async Task A_credit_sale_without_any_customer_is_refused_by_the_rules_of_the_sale()
    {
        var (world, session) = Till();

        var result = await world.RecordSale.HandleAsync(CreditSale(world, session, null, 1_000));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
    }

    [Fact]
    public async Task Replaying_a_credit_sale_does_not_charge_twice_and_two_sales_add_up_SC_C8()
    {
        var (world, session) = Till();
        var customer = world.GivenCustomer(ceiling: 50_000);
        var first = CreditSale(world, session, customer.Id, 8_000);

        await world.RecordSale.HandleAsync(first);
        await world.RecordSale.HandleAsync(first);
        await world.RecordSale.HandleAsync(CreditSale(world, session, customer.Id, 2_000, sequence: 2));

        Assert.Equal(Money.Fcfa(10_000), customer.Debt);
    }

    [Fact]
    public async Task Cancelling_a_credit_sale_cancels_its_debt_but_not_the_cash_part_SC_C7()
    {
        var (world, session) = Till();
        var customer = world.GivenCustomer(ceiling: 50_000);
        var command = CreditSale(world, session, customer.Id, credit: 3_000, cash: 5_000);
        await world.RecordSale.HandleAsync(command);
        world.ActAs(world.Manager, Role.Manager);

        var cancelled = await world.CancelSale.HandleAsync(new CancelSaleCommand(command.Id, "Erreur de saisie"));

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(Money.Zero, customer.Debt);
    }

    [Fact]
    public async Task Cancelling_a_cash_sale_leaves_the_customer_alone()
    {
        var (world, session) = Till();
        var customer = world.GivenCustomer(debt: 1_000);
        var command = world.CashSale(session) with { Customer = customer.Id };
        await world.RecordSale.HandleAsync(command);
        world.ActAs(world.Manager, Role.Manager);

        await world.CancelSale.HandleAsync(new CancelSaleCommand(command.Id, "Erreur"));

        Assert.Equal(Money.Fcfa(1_000), customer.Debt);
    }

    [Fact]
    public async Task The_debt_always_equals_the_credits_of_the_validated_sales_minus_the_repayments_CRD_07()
    {
        var (world, session) = Till();
        var customer = world.GivenCustomer(ceiling: 100_000);
        var kept = CreditSale(world, session, customer.Id, 8_000);
        var cancelled = CreditSale(world, session, customer.Id, 4_000, sequence: 2);
        await world.RecordSale.HandleAsync(kept);
        await world.RecordSale.HandleAsync(cancelled);
        await world.RecordRepayment.HandleAsync(new(DebtRepaymentId.New(), customer.Id, 3_000, PaymentMethod.Cash, null));
        world.ActAs(world.Manager, Role.Manager);
        await world.CancelSale.HandleAsync(new CancelSaleCommand(cancelled.Id, "Erreur"));

        var credits = world.Sales.All
            .Where(sale => sale.Status == SaleStatus.Validated)
            .SelectMany(sale => sale.Payments)
            .Where(payment => payment.Method == PaymentMethod.Credit)
            .Sum(payment => payment.Amount.Amount);
        var repaid = world.Repayments.All.Sum(item => item.Amount.Amount);
        Assert.Equal(credits - repaid, customer.Debt.Amount);
    }
}
