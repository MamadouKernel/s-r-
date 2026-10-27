using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Domain.Tests;

public class SaleReturnTests
{
    private static Sale Sell(IReadOnlyList<SaleLine> lines, IReadOnlyList<Payment> payments, long discount = 0, CustomerId? customer = null) =>
        TestData.OpenSession().RecordSale(TestData.Draft(lines, payments, Money.Fcfa(discount), customer));

    private static ReturnRequest Ask(int line, int quantity, bool resellable = true) => new(line, quantity, resellable);

    private static SaleReturn Done(Sale sale, ReturnPlan plan, IReadOnlyList<SaleReturn> previous) =>
        SaleReturn.Record(
            SaleReturnId.New(), sale, ReturnKind.Return, TestData.Morning.AddHours(2), UserId.New(), null, "Retour", plan, SessionId.New());

    [Fact]
    public void Returning_one_article_of_three_refunds_its_price_in_cash()
    {
        var sale = Sell([TestData.Line(1_000, 3)], [TestData.Cash(3_000)]);

        var plan = ReturnPlanner.Plan(sale, [], [Ask(0, 1)]);

        Assert.Equal(Money.Fcfa(1_000), plan.Total);
        Assert.Equal(Money.Fcfa(1_000), plan.Cash);
        Assert.Equal(PaymentMethod.Cash, Assert.Single(plan.Refund).Method);
    }

    [Fact]
    public void The_discount_of_the_sale_is_shared_out_over_the_returned_articles()
    {
        var sale = Sell([TestData.Line(1_000, 3)], [TestData.Cash(2_700)], discount: 300);

        var first = ReturnPlanner.Plan(sale, [], [Ask(0, 1)]);
        var second = ReturnPlanner.Plan(sale, [Done(sale, first, [])], [Ask(0, 2)]);

        Assert.Equal(Money.Fcfa(900), first.Total);
        Assert.Equal(Money.Fcfa(1_800), second.Total);
    }

    [Fact]
    public void Returns_one_by_one_add_up_exactly_to_what_was_paid_whatever_the_rounding()
    {
        var sale = Sell([TestData.Line(1_000, 3)], [TestData.Cash(3_000)]);
        var previous = new List<SaleReturn>();
        var refunded = new List<long>();

        for (var round = 0; round < 3; round++)
        {
            var plan = ReturnPlanner.Plan(sale, previous, [Ask(0, 1)]);
            refunded.Add(plan.Total.Amount);
            previous.Add(Done(sale, plan, previous));
        }

        Assert.Equal(3_000, refunded.Sum());
    }

    [Fact]
    public void A_weighed_product_is_refunded_for_the_exact_weight_rounded_to_the_franc()
    {
        var kilo = SaleLine.Create(ProductId.New(), "Riz", 2_000, Money.Fcfa(800), unit: MeasureUnit.Kilogram);
        var sale = Sell([kilo], [TestData.Cash(1_600)]);

        var plan = ReturnPlanner.Plan(sale, [], [Ask(0, 500)]);

        Assert.Equal(Money.Fcfa(400), plan.Total);
    }

    [Fact]
    public void Returning_more_than_what_was_sold_is_refused_counting_the_earlier_returns()
    {
        var sale = Sell([TestData.Line(1_000, 3)], [TestData.Cash(3_000)]);
        var first = ReturnPlanner.Plan(sale, [], [Ask(0, 2)]);

        var error = Assert.Throws<DomainException>(() => ReturnPlanner.Plan(sale, [Done(sale, first, [])], [Ask(0, 2)]));

        Assert.Contains("plus que ce qui a été vendu", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_line_a_zero_quantity_and_an_empty_request_are_refused()
    {
        var sale = Sell([TestData.Line(1_000)], [TestData.Cash(1_000)]);

        Assert.Throws<DomainException>(() => ReturnPlanner.Plan(sale, [], [Ask(3, 1)]));
        Assert.Throws<DomainException>(() => ReturnPlanner.Plan(sale, [], [Ask(0, 0)]));
        Assert.Throws<DomainException>(() => ReturnPlanner.Plan(sale, [], []));
    }

    [Fact]
    public void A_cancelled_sale_cannot_be_returned()
    {
        var sale = Sell([TestData.Line(1_000)], [TestData.Cash(1_000)]).Cancel(UserId.New(), "Erreur", TestData.Morning.AddHours(1));

        Assert.Throws<DomainException>(() => ReturnPlanner.Plan(sale, [], [Ask(0, 1)]));
    }

    [Fact]
    public void The_refund_starts_with_the_credit_then_the_cash_then_the_mobile_money()
    {
        var customer = CustomerId.New();
        var sale = Sell(
            [TestData.Line(1_000, 6)],
            [TestData.Cash(2_000), TestData.Mobile(2_000, method: PaymentMethod.Wave), Payment.Credit(PaymentId.New(), Money.Fcfa(2_000))],
            customer: customer);

        var small = ReturnPlanner.Plan(sale, [], [Ask(0, 1)]);
        var large = ReturnPlanner.Plan(sale, [], [Ask(0, 5)]);

        Assert.Equal([(PaymentMethod.Credit, 1_000L)], small.Refund.Select(part => (part.Method, part.Amount.Amount)).ToArray());
        Assert.Equal(
            [(PaymentMethod.Credit, 2_000L), (PaymentMethod.Cash, 2_000L), (PaymentMethod.Wave, 1_000L)],
            large.Refund.Select(part => (part.Method, part.Amount.Amount)).ToArray());
    }

    [Fact]
    public void A_method_is_never_refunded_more_than_it_was_paid_across_several_returns()
    {
        var sale = Sell([TestData.Line(1_000, 4)], [TestData.Cash(2_000), TestData.Mobile(2_000, method: PaymentMethod.Wave)]);
        var first = ReturnPlanner.Plan(sale, [], [Ask(0, 3)]);
        var previous = new List<SaleReturn> { Done(sale, first, []) };

        var second = ReturnPlanner.Plan(sale, previous, [Ask(0, 1)]);

        Assert.Equal(Money.Fcfa(2_000), first.Cash);
        Assert.Equal([(PaymentMethod.Wave, 1_000L)], second.Refund.Select(part => (part.Method, part.Amount.Amount)).ToArray());
    }

    [Fact]
    public void A_refund_in_cash_lowers_the_cash_expected_in_the_open_session()
    {
        var session = TestData.OpenSession(openingFloat: 10_000);
        session.RecordSale(TestData.Draft([TestData.Line(1_000, 3)], [TestData.Cash(3_000)]));

        session.RecordCashRefund(Money.Fcfa(1_000));

        Assert.Equal(Money.Fcfa(1_000), session.CashRefunded);
        Assert.Equal(Money.Fcfa(12_000), session.ExpectedCash);
    }

    [Fact]
    public void A_closed_session_takes_no_refund_and_a_negative_refund_is_refused()
    {
        var session = TestData.OpenSession();
        Assert.Throws<DomainException>(() => session.RecordCashRefund(Money.Fcfa(-5)));
        session.Close(Money.Fcfa(10_000), TestData.Morning.AddHours(8));

        Assert.Throws<DomainException>(() => session.RecordCashRefund(Money.Fcfa(100)));
    }

    [Fact]
    public void A_cancellation_returns_every_remaining_article_and_a_sale_with_a_return_cannot_be_cancelled_whole()
    {
        var sale = Sell([TestData.Line(1_000, 2), TestData.Line(500)], [TestData.Cash(2_500)]);

        var plan = ReturnPlanner.PlanAll(sale, [], resellable: true);

        Assert.Equal(Money.Fcfa(2_500), plan.Total);
        Assert.Equal([(0, 2), (1, 1)], plan.Lines.Select(line => (line.LineIndex, line.Quantity)).ToArray());
        var earlier = Done(sale, ReturnPlanner.Plan(sale, [], [Ask(0, 1)]), []);
        Assert.Throws<DomainException>(() => ReturnPlanner.PlanAll(sale, [earlier], resellable: true));
    }
}
