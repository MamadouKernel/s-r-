using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Domain.Tests;

public class SaleTests
{
    [Fact]
    public void Simple_cash_sale_computes_totals_and_change()
    {
        var session = TestData.OpenSession();
        var draft = TestData.Draft(
            [TestData.Line(500, 2), TestData.Line(1_200), TestData.Line(300)],
            [TestData.Cash(2_500, tendered: 3_000)]);

        var sale = session.RecordSale(draft);

        Assert.Equal(Money.Fcfa(2_500), sale.GrossTotal);
        Assert.Equal(Money.Fcfa(2_500), sale.NetTotal);
        Assert.Equal(Money.Fcfa(500), Assert.Single(sale.Payments).Change);
        Assert.Equal(SaleStatus.Validated, sale.Status);
    }

    [Fact]
    public void Sale_is_attributed_to_tenant_point_of_sale_register_session_and_cashier()
    {
        var session = TestData.OpenSession();

        var sale = session.RecordSale(TestData.Draft([TestData.Line(500)], [TestData.Cash(500)]));

        Assert.Equal(session.Id, sale.SessionId);
        Assert.Equal(session.Assignment, sale.Assignment);
    }

    [Fact]
    public void Discount_reduces_the_net_total()
    {
        var session = TestData.OpenSession();
        var draft = TestData.Draft(
            [TestData.Line(1_000)], [TestData.Cash(900)], discount: Money.Fcfa(100));

        var sale = session.RecordSale(draft);

        Assert.Equal(Money.Fcfa(900), sale.NetTotal);
    }

    [Fact]
    public void Discount_cannot_exceed_the_gross_total()
    {
        var session = TestData.OpenSession();
        var draft = TestData.Draft(
            [TestData.Line(1_000)], [TestData.Cash(500)], discount: Money.Fcfa(1_500));

        Assert.Throws<DomainException>(() => session.RecordSale(draft));
    }

    [Fact]
    public void Net_total_must_be_greater_than_zero()
    {
        var session = TestData.OpenSession();
        var draft = TestData.Draft(
            [TestData.Line(1_000)], [TestData.Cash(1_000)], discount: Money.Fcfa(1_000));

        Assert.Throws<DomainException>(() => session.RecordSale(draft));
    }

    [Fact]
    public void Payments_must_add_up_to_the_net_total()
    {
        var session = TestData.OpenSession();
        var draft = TestData.Draft([TestData.Line(1_000)], [TestData.Cash(900)]);

        Assert.Throws<DomainException>(() => session.RecordSale(draft));
    }

    [Fact]
    public void Mixed_payment_is_accepted_when_it_adds_up()
    {
        var session = TestData.OpenSession();
        var draft = TestData.Draft(
            [TestData.Line(3_000)], [TestData.Cash(1_000), TestData.Mobile(2_000)]);

        var sale = session.RecordSale(draft);

        Assert.Equal(2, sale.Payments.Count);
        Assert.Equal(Money.Fcfa(1_000), sale.CashReceived);
    }

    [Fact]
    public void Sale_requires_at_least_one_line() =>
        Assert.Throws<DomainException>(() =>
            TestData.OpenSession().RecordSale(TestData.Draft([], [TestData.Cash(500)])));

    [Fact]
    public void Sale_requires_at_least_one_payment() =>
        Assert.Throws<DomainException>(() =>
            TestData.OpenSession().RecordSale(TestData.Draft([TestData.Line(500)], [])));

    [Fact]
    public void Credit_requires_a_customer()
    {
        var session = TestData.OpenSession();
        var credit = Payment.Credit(PaymentId.New(), Money.Fcfa(500));

        Assert.Throws<DomainException>(() =>
            session.RecordSale(TestData.Draft([TestData.Line(500)], [credit])));
    }

    [Fact]
    public void Credit_is_accepted_with_a_customer()
    {
        var session = TestData.OpenSession();
        var credit = Payment.Credit(PaymentId.New(), Money.Fcfa(500));
        var customer = CustomerId.New();

        var sale = session.RecordSale(TestData.Draft([TestData.Line(500)], [credit], customer: customer));

        Assert.Equal(customer, sale.CustomerId);
    }

    [Fact]
    public void Same_mobile_money_reference_cannot_be_used_twice_in_one_sale()
    {
        var session = TestData.OpenSession();
        var draft = TestData.Draft(
            [TestData.Line(3_000)],
            [TestData.Mobile(1_000, "REF-1"), TestData.Mobile(2_000, "ref-1")]);

        Assert.Throws<DomainException>(() => session.RecordSale(draft));
    }

    [Fact]
    public void Sale_keeps_its_own_copy_of_the_lines()
    {
        var session = TestData.OpenSession();
        var lines = new List<SaleLine> { TestData.Line(500) };

        var sale = session.RecordSale(TestData.Draft(lines, [TestData.Cash(500)]));
        lines.Add(TestData.Line(9_000));

        Assert.Single(sale.Lines);
    }

    [Fact]
    public void Cancellation_returns_a_new_sale_and_keeps_the_original_validated()
    {
        var session = TestData.OpenSession();
        var sale = session.RecordSale(TestData.Draft([TestData.Line(500)], [TestData.Cash(500)]));
        var manager = UserId.New();

        var cancelled = sale.Cancel(manager, "Erreur de saisie", TestData.Morning.AddMinutes(30));

        Assert.Equal(SaleStatus.Cancelled, cancelled.Status);
        Assert.Equal(SaleStatus.Validated, sale.Status);
        Assert.Equal(manager, cancelled.Cancellation!.CancelledBy);
        Assert.Equal("Erreur de saisie", cancelled.Cancellation.Reason);
    }

    [Fact]
    public void Cancellation_requires_a_reason()
    {
        var session = TestData.OpenSession();
        var sale = session.RecordSale(TestData.Draft([TestData.Line(500)], [TestData.Cash(500)]));

        Assert.Throws<DomainException>(() =>
            sale.Cancel(UserId.New(), " ", TestData.Morning.AddMinutes(30)));
    }

    [Fact]
    public void A_sale_cannot_be_cancelled_twice()
    {
        var session = TestData.OpenSession();
        var sale = session.RecordSale(TestData.Draft([TestData.Line(500)], [TestData.Cash(500)]));
        var cancelled = sale.Cancel(UserId.New(), "Erreur", TestData.Morning.AddMinutes(30));

        Assert.Throws<DomainException>(() =>
            cancelled.Cancel(UserId.New(), "Encore", TestData.Morning.AddMinutes(31)));
    }

    [Fact]
    public void Cancellation_cannot_precede_the_sale()
    {
        var session = TestData.OpenSession();
        var sale = session.RecordSale(TestData.Draft([TestData.Line(500)], [TestData.Cash(500)]));

        Assert.Throws<DomainException>(() =>
            sale.Cancel(UserId.New(), "Erreur", TestData.Morning));
    }
}
