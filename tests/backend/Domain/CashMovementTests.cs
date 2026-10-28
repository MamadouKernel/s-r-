using Soro.Domain.Cash;
using Soro.Domain.Common;

namespace Soro.Domain.Tests;

/// <summary>Sorties et entrées d'espèces, clôture à deux comptages (docs/specs/cloture-sorties-rapport CLO-01, CLO-02, CLO-04).</summary>
public class CashMovementTests
{
    private static readonly Money Tolerance = Money.Fcfa(500);

    [Fact]
    public void An_expense_a_drop_and_a_topup_move_the_cash_expected_in_the_drawer()
    {
        var session = TestData.OpenSession(openingFloat: 10_000);
        session.RecordSale(TestData.Draft([TestData.Line(5_000)], [TestData.Cash(5_000)]));

        session.RecordCashMovement(CashMovementKind.Expense, Money.Fcfa(1_500));
        session.RecordCashMovement(CashMovementKind.Drop, Money.Fcfa(8_000));
        session.RecordCashMovement(CashMovementKind.Topup, Money.Fcfa(2_000));

        Assert.Equal(Money.Fcfa(9_500), session.CashPaidOut);
        Assert.Equal(Money.Fcfa(2_000), session.CashPaidIn);
        Assert.Equal(Money.Fcfa(7_500), session.ExpectedCash);
    }

    [Fact]
    public void A_refund_and_an_expense_both_come_out_of_the_same_drawer()
    {
        var session = TestData.OpenSession(openingFloat: 10_000);
        session.RecordCashRefund(Money.Fcfa(1_000));

        session.RecordCashMovement(CashMovementKind.Expense, Money.Fcfa(500));

        Assert.Equal(Money.Fcfa(8_500), session.ExpectedCash);
    }

    [Fact]
    public void Money_cannot_go_out_that_the_drawer_does_not_hold()
    {
        var session = TestData.OpenSession(openingFloat: 1_000);

        var failure = Assert.Throws<DomainException>(() => session.RecordCashMovement(CashMovementKind.Drop, Money.Fcfa(1_001)));

        Assert.Contains("dépasse l'argent", failure.Message, StringComparison.Ordinal);
        Assert.Equal(Money.Fcfa(1_000), session.ExpectedCash);
        session.RecordCashMovement(CashMovementKind.Drop, Money.Fcfa(1_000));
        Assert.Equal(Money.Zero, session.ExpectedCash);
    }

    [Fact]
    public void A_movement_needs_a_positive_amount_and_an_open_session()
    {
        var session = TestData.OpenSession();

        Assert.Throws<DomainException>(() => session.RecordCashMovement(CashMovementKind.Topup, Money.Zero));
        session.Close(session.ExpectedCash, TestData.Morning.AddHours(8));
        Assert.Throws<DomainException>(() => session.RecordCashMovement(CashMovementKind.Topup, Money.Fcfa(100)));
    }

    [Fact]
    public void A_count_within_the_tolerance_closes_the_session_and_keeps_the_variance()
    {
        var session = TestData.OpenSession(openingFloat: 10_000);

        var outcome = session.Count(Money.Fcfa(9_700), TestData.Morning.AddHours(8), Tolerance, isRecount: false);

        Assert.Equal(CountOutcome.Closed, outcome);
        Assert.Equal(SessionStatus.Closed, session.Status);
        Assert.Equal(-300, session.CashVariance);
        Assert.Null(session.FirstCount);
    }

    [Fact]
    public void A_count_beyond_the_tolerance_asks_for_a_recount_and_keeps_the_session_open()
    {
        var session = TestData.OpenSession(openingFloat: 10_000);

        var outcome = session.Count(Money.Fcfa(12_000), TestData.Morning.AddHours(8), Tolerance, isRecount: false);

        Assert.Equal(CountOutcome.NeedsRecount, outcome);
        Assert.Equal(SessionStatus.Open, session.Status);
        Assert.Equal(Money.Fcfa(12_000), session.FirstCount);
        Assert.Null(session.CashVariance);
    }

    [Theory]
    [InlineData(10_500)]
    [InlineData(9_500)]
    public void The_tolerance_is_inclusive_in_both_directions(long counted)
    {
        var session = TestData.OpenSession(openingFloat: 10_000);

        Assert.Equal(CountOutcome.Closed, session.Count(Money.Fcfa(counted), TestData.Morning.AddHours(8), Tolerance, isRecount: false));
    }

    [Fact]
    public void The_recount_is_final_whatever_the_gap_and_both_counts_are_kept()
    {
        var session = TestData.OpenSession(openingFloat: 10_000);
        session.Count(Money.Fcfa(12_000), TestData.Morning.AddHours(8), Tolerance, isRecount: false);

        var outcome = session.Count(Money.Fcfa(8_000), TestData.Morning.AddHours(8).AddMinutes(5), Tolerance, isRecount: true);

        Assert.Equal(CountOutcome.Closed, outcome);
        Assert.Equal(Money.Fcfa(8_000), session.CountedCash);
        Assert.Equal(Money.Fcfa(12_000), session.FirstCount);
        Assert.Equal(-2_000, session.CashVariance);
    }

    [Fact]
    public void A_recount_without_a_first_count_is_simply_the_final_count()
    {
        var session = TestData.OpenSession(openingFloat: 10_000);

        Assert.Equal(CountOutcome.Closed, session.Count(Money.Fcfa(14_000), TestData.Morning.AddHours(8), Tolerance, isRecount: true));
        Assert.Equal(4_000, session.CashVariance);
    }

    [Fact]
    public void An_expense_changes_what_is_expected_so_that_an_honest_count_has_no_variance()
    {
        var session = TestData.OpenSession(openingFloat: 10_000);
        session.RecordCashMovement(CashMovementKind.Expense, Money.Fcfa(1_500));

        session.Count(Money.Fcfa(8_500), TestData.Morning.AddHours(8), Tolerance, isRecount: false);

        Assert.Equal(0, session.CashVariance);
    }

    [Fact]
    public void An_expense_needs_a_reason_and_a_movement_has_a_bounded_one()
    {
        var movement = (CashMovementKind kind, string? reason) => CashMovement.Record(
            CashMovementId.New(), TenantId.New(), SessionId.New(), kind, Money.Fcfa(1_500), reason, TestData.Morning, UserId.New(), null);

        Assert.Throws<DomainException>(() => movement(CashMovementKind.Expense, null));
        Assert.Throws<DomainException>(() => movement(CashMovementKind.Expense, "  "));
        Assert.Throws<DomainException>(() => movement(CashMovementKind.Drop, new string('x', 201)));
        Assert.Equal("Transport", movement(CashMovementKind.Expense, "  Transport ").Reason);
        Assert.Null(movement(CashMovementKind.Drop, null).Reason);
    }
}
