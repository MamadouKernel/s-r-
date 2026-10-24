using Soro.Domain.Common;
using Soro.Domain.Customers;
using Soro.Domain.Sales;

namespace Soro.Domain.Tests;

public class CustomerTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.New();

    private static Customer Given(string name = "Awa Koné", string? phone = "07 07 07 07 07", long ceiling = 20_000) =>
        Customer.Create(CustomerId.New(), Tenant, name, phone, Money.Fcfa(ceiling), At);

    [Fact]
    public void A_new_customer_is_active_with_no_debt_and_the_whole_ceiling_available()
    {
        var customer = Given();

        Assert.True(customer.IsActive);
        Assert.Equal(Money.Zero, customer.Debt);
        Assert.Equal(Money.Fcfa(20_000), customer.AvailableCredit);
        Assert.Equal("+2250707070707", customer.Phone!.Value.Value);
    }

    [Fact]
    public void The_phone_is_optional_and_the_name_is_trimmed()
    {
        var customer = Given("  Awa Koné  ", null);

        Assert.Null(customer.Phone);
        Assert.Equal("Awa Koné", customer.Name);
        Assert.Null(Given(phone: "   ").Phone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("   ")]
    public void Refuses_a_name_that_is_too_short(string name) => Assert.Throws<DomainException>(() => Given(name));

    [Fact]
    public void Refuses_a_name_over_100_characters() => Assert.Throws<DomainException>(() => Given(new string('a', 101)));

    [Fact]
    public void Refuses_a_phone_that_is_not_ivorian() => Assert.Throws<DomainException>(() => Given(phone: "12345"));

    [Fact]
    public void A_charge_raises_the_debt_and_lowers_the_available_credit_SC_C2()
    {
        var customer = Given();

        customer.Charge(Money.Fcfa(8_000));

        Assert.Equal(Money.Fcfa(8_000), customer.Debt);
        Assert.Equal(Money.Fcfa(12_000), customer.AvailableCredit);
    }

    [Theory]
    [InlineData(8_000, 12_000, false)] // pile au plafond
    [InlineData(8_000, 12_001, true)]
    [InlineData(0, 20_001, true)]
    [InlineData(0, 20_000, false)]
    public void Tells_whether_a_charge_would_exceed_the_ceiling(long debt, long charge, bool exceeds)
    {
        var customer = Given();
        if (debt > 0)
        {
            customer.Charge(Money.Fcfa(debt));
        }

        Assert.Equal(exceeds, customer.WouldExceedCeiling(Money.Fcfa(charge)));
    }

    [Fact]
    public void A_charge_may_pass_the_ceiling_when_the_caller_decides_so_and_the_credit_is_then_zero()
    {
        var customer = Given(ceiling: 10_000);

        customer.Charge(Money.Fcfa(15_000)); // validation d'un gérant, ou vente hors ligne déjà faite

        Assert.Equal(Money.Fcfa(15_000), customer.Debt);
        Assert.Equal(Money.Zero, customer.AvailableCredit);
    }

    [Fact]
    public void A_charge_must_be_positive() => Assert.Throws<DomainException>(() => Given().Charge(Money.Zero));

    [Fact]
    public void A_repayment_lowers_the_debt_SC_C5()
    {
        var customer = Given();
        customer.Charge(Money.Fcfa(8_000));

        customer.Repay(Money.Fcfa(5_000));

        Assert.Equal(Money.Fcfa(3_000), customer.Debt);
    }

    [Fact]
    public void A_repayment_cannot_exceed_the_debt_nor_be_zero()
    {
        var customer = Given();
        customer.Charge(Money.Fcfa(8_000));

        Assert.Throws<DomainException>(() => customer.Repay(Money.Fcfa(8_001)));
        Assert.Throws<DomainException>(() => customer.Repay(Money.Zero));
        Assert.Equal(Money.Fcfa(8_000), customer.Debt);
        customer.Repay(Money.Fcfa(8_000));
        Assert.Equal(Money.Zero, customer.Debt);
    }

    [Fact]
    public void Cancelling_a_credit_sale_lowers_the_debt_but_never_below_zero_SC_C7()
    {
        var customer = Given();
        customer.Charge(Money.Fcfa(8_000));
        customer.Repay(Money.Fcfa(6_000));

        customer.Discharge(Money.Fcfa(8_000)); // le client avait déjà payé 6 000 : la dette tombe à 0, pas à -6 000

        Assert.Equal(Money.Zero, customer.Debt);
    }

    [Fact]
    public void Editing_changes_the_name_the_phone_and_the_ceiling_and_keeps_the_debt()
    {
        var customer = Given();
        customer.Charge(Money.Fcfa(8_000));

        customer.Edit("Awa Koné-Traoré", "01 02 03 04 05", Money.Fcfa(50_000));

        Assert.Equal("Awa Koné-Traoré", customer.Name);
        Assert.Equal("+2250102030405", customer.Phone!.Value.Value);
        Assert.Equal(Money.Fcfa(50_000), customer.CreditCeiling);
        Assert.Equal(Money.Fcfa(8_000), customer.Debt);
    }

    [Fact]
    public void Lowering_the_ceiling_below_the_debt_is_allowed_and_blocks_new_credit()
    {
        var customer = Given();
        customer.Charge(Money.Fcfa(8_000));

        customer.Edit(customer.Name, null, Money.Fcfa(5_000));

        Assert.Equal(Money.Zero, customer.AvailableCredit);
        Assert.True(customer.WouldExceedCeiling(Money.Fcfa(1)));
    }

    [Fact]
    public void A_customer_with_a_debt_cannot_be_deactivated_SC_C9()
    {
        var customer = Given();
        customer.Charge(Money.Fcfa(100));

        Assert.Throws<DomainException>(customer.Deactivate);
        Assert.True(customer.IsActive);

        customer.Repay(Money.Fcfa(100));
        customer.Deactivate();
        Assert.False(customer.IsActive);
    }

    [Fact]
    public void A_deactivated_customer_takes_no_new_credit()
    {
        var customer = Given();
        customer.Deactivate();

        Assert.Throws<DomainException>(() => customer.Charge(Money.Fcfa(100)));
    }
}

public class DebtRepaymentTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private static DebtRepayment Cash(long amount) =>
        DebtRepayment.Record(DebtRepaymentId.New(), TenantId.New(), CustomerId.New(), Money.Fcfa(amount), PaymentMethod.Cash, null, UserId.New(), At);

    [Fact]
    public void Records_who_how_much_how_and_when()
    {
        var by = UserId.New();
        var repayment = DebtRepayment.Record(
            DebtRepaymentId.New(), TenantId.New(), CustomerId.New(), Money.Fcfa(5_000), PaymentMethod.Wave, "  WV-123 ", by, At);

        Assert.Equal(Money.Fcfa(5_000), repayment.Amount);
        Assert.Equal(PaymentMethod.Wave, repayment.Method);
        Assert.Equal("WV-123", repayment.Reference);
        Assert.Equal(by, repayment.User);
        Assert.Equal(At, repayment.At);
    }

    [Fact]
    public void A_cash_repayment_has_no_reference_even_if_one_is_given()
    {
        var repayment = DebtRepayment.Record(
            DebtRepaymentId.New(), TenantId.New(), CustomerId.New(), Money.Fcfa(1_000), PaymentMethod.Cash, "ignorée", UserId.New(), At);

        Assert.Null(repayment.Reference);
    }

    [Fact]
    public void Mobile_money_needs_a_reference_SC_C6() =>
        Assert.Throws<DomainException>(() => DebtRepayment.Record(
            DebtRepaymentId.New(), TenantId.New(), CustomerId.New(), Money.Fcfa(1_000), PaymentMethod.OrangeMoney, "  ", UserId.New(), At));

    [Fact]
    public void Credit_is_not_a_way_to_repay() =>
        Assert.Throws<DomainException>(() => DebtRepayment.Record(
            DebtRepaymentId.New(), TenantId.New(), CustomerId.New(), Money.Fcfa(1_000), PaymentMethod.Credit, null, UserId.New(), At));

    [Fact]
    public void The_amount_must_be_positive() => Assert.Throws<DomainException>(() => Cash(0));
}
