using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Subscription;
using Soro.Domain.Tenancy;

namespace Soro.Domain.Tests;

public class SubscriptionStateTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TrialEnd = Start.AddDays(30);

    [Fact]
    public void During_the_trial_with_nothing_paid_the_state_is_trial_and_counts_the_days_left()
    {
        var state = SubscriptionRules.StateAt(TrialEnd, null, Start.AddDays(25));

        Assert.Equal(SubscriptionStatus.Trial, state.Status);
        Assert.Equal(TrialEnd, state.EndsAt);
        Assert.Equal(5, state.DaysLeft);
        Assert.False(state.IsReadOnly);
    }

    [Fact]
    public void The_last_day_still_counts_as_one_day_left_and_the_end_instant_starts_the_grace()
    {
        Assert.Equal(1, SubscriptionRules.StateAt(TrialEnd, null, TrialEnd.AddHours(-3)).DaysLeft);
        Assert.Equal(SubscriptionStatus.Trial, SubscriptionRules.StateAt(TrialEnd, null, TrialEnd.AddTicks(-1)).Status);
        Assert.Equal(SubscriptionStatus.Grace, SubscriptionRules.StateAt(TrialEnd, null, TrialEnd).Status);
    }

    [Fact]
    public void After_the_end_there_are_seven_days_of_grace_then_read_only()
    {
        var inGrace = SubscriptionRules.StateAt(TrialEnd, null, TrialEnd.AddDays(4));
        var lastGraceInstant = SubscriptionRules.StateAt(TrialEnd, null, TrialEnd.AddDays(7).AddTicks(-1));
        var readOnly = SubscriptionRules.StateAt(TrialEnd, null, TrialEnd.AddDays(7));

        Assert.Equal(SubscriptionStatus.Grace, inGrace.Status);
        Assert.Equal(3, inGrace.DaysLeft);
        Assert.Equal(SubscriptionStatus.Grace, lastGraceInstant.Status);
        Assert.Equal(SubscriptionStatus.ReadOnly, readOnly.Status);
        Assert.Equal(0, readOnly.DaysLeft);
        Assert.True(readOnly.IsReadOnly);
        Assert.Equal(TrialEnd.AddDays(7), readOnly.ReadOnlyFrom);
    }

    [Fact]
    public void A_paid_period_makes_the_state_active_until_its_end_then_follows_the_same_grace()
    {
        var paidUntil = TrialEnd.AddMonths(1);

        var active = SubscriptionRules.StateAt(TrialEnd, paidUntil, TrialEnd.AddDays(10));
        var grace = SubscriptionRules.StateAt(TrialEnd, paidUntil, paidUntil.AddDays(2));
        var readOnly = SubscriptionRules.StateAt(TrialEnd, paidUntil, paidUntil.AddDays(8));

        Assert.Equal(SubscriptionStatus.Active, active.Status);
        Assert.Equal(paidUntil, active.EndsAt);
        Assert.Equal(SubscriptionStatus.Grace, grace.Status);
        Assert.Equal(SubscriptionStatus.ReadOnly, readOnly.Status);
    }

    [Fact]
    public void Paying_during_the_trial_keeps_the_remaining_trial_days()
    {
        var now = Start.AddDays(20);
        var from = SubscriptionRules.CoverageStart(TrialEnd, null, now);

        Assert.Equal(TrialEnd, from);
        Assert.Equal(TrialEnd.AddMonths(1), SubscriptionPeriod.Monthly.AddTo(from));
    }

    [Fact]
    public void Paying_late_starts_the_new_period_today_so_no_day_is_offered()
    {
        var now = TrialEnd.AddDays(40);

        Assert.Equal(now, SubscriptionRules.CoverageStart(TrialEnd, TrialEnd.AddMonths(1), now));
    }

    [Fact]
    public void Paying_in_advance_adds_the_period_after_the_current_end()
    {
        var paidUntil = TrialEnd.AddMonths(1);
        var now = paidUntil.AddDays(-10);

        Assert.Equal(paidUntil, SubscriptionRules.CoverageStart(TrialEnd, paidUntil, now));
    }

    [Fact]
    public void An_annual_period_covers_twelve_months()
    {
        var from = new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2027, 11, 30, 0, 0, 0, TimeSpan.Zero), SubscriptionPeriod.Annual.AddTo(from));
        Assert.Equal(new DateTimeOffset(2026, 12, 30, 0, 0, 0, TimeSpan.Zero), SubscriptionPeriod.Monthly.AddTo(from));
    }

    [Fact]
    public void A_commerce_reports_its_state_and_extends_its_paid_period()
    {
        var commerce = Commerce.Open(TenantId.New(), "Boutique", ActivityType.Shop, EmailAddress.Parse("a@exemple.ci"), "v1", Start);

        Assert.Null(commerce.PaidUntil);
        Assert.Equal(SubscriptionStatus.Trial, commerce.SubscriptionStateAt(Start.AddDays(1)).Status);

        commerce.ExtendPaidUntil(TrialEnd.AddMonths(1));

        Assert.Equal(SubscriptionStatus.Active, commerce.SubscriptionStateAt(Start.AddDays(1)).Status);
        Assert.Equal(TrialEnd.AddMonths(1), commerce.PaidUntil);
    }

    [Fact]
    public void A_paid_period_can_only_move_forward()
    {
        var commerce = Commerce.Open(TenantId.New(), "Boutique", ActivityType.Shop, EmailAddress.Parse("a@exemple.ci"), "v1", Start);
        commerce.ExtendPaidUntil(TrialEnd.AddMonths(2));

        Assert.Throws<DomainException>(() => commerce.ExtendPaidUntil(TrialEnd.AddMonths(1)));
    }
}

public class SubscriptionPaymentTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static SubscriptionPayment Declare(string reference = "W-123 456", long amount = 10_000, SubscriptionPeriod period = SubscriptionPeriod.Monthly) =>
        SubscriptionPayment.Declare(SubscriptionPaymentId.New(), TenantId.New(), period, SubscriptionMethod.Wave, reference, amount, At);

    [Fact]
    public void A_declared_payment_waits_for_validation_and_covers_nothing_yet()
    {
        var payment = Declare();

        Assert.Equal(SubscriptionPaymentStatus.Pending, payment.Status);
        Assert.Null(payment.CoveredTo);
        Assert.Equal("W-123 456", payment.Reference);
    }

    [Theory]
    [InlineData("W-123 456", "W123456")]
    [InlineData("w123456", "W123456")]
    [InlineData("  MP.2610/ab ", "MP2610AB")]
    public void The_reference_key_ignores_case_spaces_and_punctuation(string typed, string key) =>
        Assert.Equal(key, Declare(typed).ReferenceKey);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    [InlineData("--")]
    public void A_reference_is_required_and_must_have_a_few_characters(string reference) =>
        Assert.Throws<DomainException>(() => Declare(reference));

    [Fact]
    public void A_reference_cannot_be_longer_than_one_hundred_characters() =>
        Assert.Throws<DomainException>(() => Declare(new string('x', 101)));

    [Fact]
    public void The_amount_must_be_positive() => Assert.Throws<DomainException>(() => Declare(amount: 0));

    [Fact]
    public void Validating_records_the_covered_period_and_the_decision_time()
    {
        var payment = Declare();
        var from = At.AddDays(3);

        payment.Validate(from, At.AddDays(1));

        Assert.Equal(SubscriptionPaymentStatus.Validated, payment.Status);
        Assert.Equal(from, payment.CoveredFrom);
        Assert.Equal(from.AddMonths(1), payment.CoveredTo);
        Assert.Equal(At.AddDays(1), payment.DecidedAt);
    }

    [Fact]
    public void An_annual_payment_covers_twelve_months()
    {
        var payment = Declare(period: SubscriptionPeriod.Annual);

        payment.Validate(At, At);

        Assert.Equal(At.AddMonths(12), payment.CoveredTo);
    }

    [Fact]
    public void Refusing_needs_a_reason_and_keeps_it()
    {
        var payment = Declare();

        Assert.Throws<DomainException>(() => payment.Refuse("  ", At));
        payment.Refuse("Référence introuvable sur le relevé.", At.AddHours(2));

        Assert.Equal(SubscriptionPaymentStatus.Refused, payment.Status);
        Assert.Equal("Référence introuvable sur le relevé.", payment.Reason);
        Assert.Null(payment.CoveredTo);
    }

    [Fact]
    public void A_decision_is_final()
    {
        var validated = Declare();
        validated.Validate(At, At);
        var refused = Declare();
        refused.Refuse("Montant faux.", At);

        Assert.Throws<DomainException>(() => validated.Validate(At, At));
        Assert.Throws<DomainException>(() => validated.Refuse("Trop tard.", At));
        Assert.Throws<DomainException>(() => refused.Validate(At, At));
    }
}
