using Soro.Domain.Cash;
using Soro.Domain.Common;

namespace Soro.Domain.Tests;

public class CashRegisterSessionTests
{
    [Fact]
    public void Opens_with_its_opening_float_as_expected_cash()
    {
        var session = TestData.OpenSession(openingFloat: 10_000);

        Assert.Equal(SessionStatus.Open, session.Status);
        Assert.Equal(Money.Fcfa(10_000), session.ExpectedCash);
    }

    [Fact]
    public void Expected_cash_counts_only_cash_kept_after_change()
    {
        var session = TestData.OpenSession(openingFloat: 10_000);

        session.RecordSale(TestData.Draft([TestData.Line(2_300)], [TestData.Cash(2_300, tendered: 5_000)]));
        session.RecordSale(TestData.Draft([TestData.Line(4_000)], [TestData.Mobile(4_000)]));

        Assert.Equal(Money.Fcfa(12_300), session.ExpectedCash);
    }

    [Fact]
    public void Refuses_a_sale_dated_before_the_opening()
    {
        var session = TestData.OpenSession();
        var draft = TestData.Draft([TestData.Line(500)], [TestData.Cash(500)]) with
        {
            At = TestData.Morning.AddMinutes(-1),
        };

        Assert.Throws<DomainException>(() => session.RecordSale(draft));
    }

    [Fact]
    public void Refuses_a_sale_once_closed()
    {
        var session = TestData.OpenSession();
        session.Close(Money.Fcfa(10_000), TestData.Morning.AddHours(8));

        Assert.Throws<DomainException>(() =>
            session.RecordSale(TestData.Draft([TestData.Line(500)], [TestData.Cash(500)])));
    }

    [Fact]
    public void Close_records_a_shortage_as_a_negative_variance()
    {
        var session = TestData.OpenSession(openingFloat: 10_000);
        session.RecordSale(TestData.Draft([TestData.Line(2_000)], [TestData.Cash(2_000)]));

        session.Close(Money.Fcfa(11_500), TestData.Morning.AddHours(8));

        Assert.Equal(SessionStatus.Closed, session.Status);
        Assert.Equal(-500L, session.CashVariance);
        Assert.Equal(Money.Fcfa(11_500), session.CountedCash);
    }

    [Fact]
    public void Close_records_a_surplus_as_a_positive_variance()
    {
        var session = TestData.OpenSession(openingFloat: 10_000);

        session.Close(Money.Fcfa(10_200), TestData.Morning.AddHours(8));

        Assert.Equal(200L, session.CashVariance);
    }

    [Fact]
    public void Cannot_be_closed_twice()
    {
        var session = TestData.OpenSession();
        session.Close(Money.Fcfa(10_000), TestData.Morning.AddHours(8));

        Assert.Throws<DomainException>(() =>
            session.Close(Money.Fcfa(10_000), TestData.Morning.AddHours(9)));
    }

    [Fact]
    public void Cannot_be_closed_before_it_opened()
    {
        var session = TestData.OpenSession();

        Assert.Throws<DomainException>(() =>
            session.Close(Money.Fcfa(10_000), TestData.Morning.AddMinutes(-1)));
    }
}
