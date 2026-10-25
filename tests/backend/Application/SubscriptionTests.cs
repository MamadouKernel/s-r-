using Soro.Application.Common;
using Soro.Application.Subscription;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Subscription;

namespace Soro.Application.Tests;

public class SubscriptionTests
{
    private static async Task<TestWorld> OwnerWorldAsync()
    {
        var world = new TestWorld();
        await world.GivenCommerceAsync();
        world.ActAs(world.Owner, Role.Owner);
        return world;
    }

    private static DeclareSubscriptionPaymentCommand Declare(
        SubscriptionPeriod period = SubscriptionPeriod.Monthly,
        string reference = "W-123456",
        long amount = 10_000,
        SubscriptionMethod method = SubscriptionMethod.Wave,
        SubscriptionPaymentId? id = null) =>
        new(id ?? SubscriptionPaymentId.New(), period, method, reference, amount);

    private static async Task<SubscriptionPaymentId> DeclaredAsync(TestWorld world, string reference = "W-123456", SubscriptionPeriod period = SubscriptionPeriod.Monthly)
    {
        var id = SubscriptionPaymentId.New();
        var result = await world.DeclareSubscriptionPayment.HandleAsync(Declare(period, reference, period == SubscriptionPeriod.Annual ? 100_000 : 10_000, id: id));
        Assert.True(result.IsSuccess, result.Error?.Message);
        return id;
    }

    // ABO-03
    [Fact]
    public async Task The_owner_reads_the_state_the_prices_how_to_pay_and_the_payments()
    {
        var world = await OwnerWorldAsync();
        world.Clock.UtcNow += TimeSpan.FromDays(25);
        await DeclaredAsync(world);

        var view = (await world.GetSubscription.HandleAsync()).Value!;

        Assert.Equal(SubscriptionStatus.Trial, view.Status);
        Assert.Equal(5, view.DaysLeft);
        Assert.Equal(10_000, view.MonthlyPriceFcfa);
        Assert.Equal(100_000, view.AnnualPriceFcfa);
        Assert.Equal("07 00 00 00 01", view.PayNumbers![SubscriptionMethod.Wave]);
        var payment = Assert.Single(view.Payments!);
        Assert.Equal(SubscriptionPaymentStatus.Pending, payment.Status);
        Assert.Equal("W-123456", payment.Reference);
    }

    [Fact]
    public async Task A_cashier_only_sees_the_state_without_prices_numbers_or_payments()
    {
        var world = await OwnerWorldAsync();
        await DeclaredAsync(world);
        world.ActAs(world.Cashier, Role.Cashier);

        var view = (await world.GetSubscription.HandleAsync()).Value!;

        Assert.Equal(SubscriptionStatus.Trial, view.Status);
        Assert.Null(view.MonthlyPriceFcfa);
        Assert.Null(view.PayNumbers);
        Assert.Null(view.Payments);
    }

    [Fact]
    public async Task A_manager_reads_the_details_but_cannot_declare_a_payment()
    {
        var world = await OwnerWorldAsync();
        world.ActAs(world.Manager, Role.Manager);

        Assert.NotNull((await world.GetSubscription.HandleAsync()).Value!.Payments);
        var declared = await world.DeclareSubscriptionPayment.HandleAsync(Declare());

        Assert.Equal(ErrorCodes.Forbidden, declared.Error!.Code);
    }

    [Fact]
    public async Task The_state_follows_the_clock_through_grace_and_read_only()
    {
        var world = await OwnerWorldAsync();

        world.Clock.UtcNow += TimeSpan.FromDays(33);
        var grace = (await world.GetSubscription.HandleAsync()).Value!;
        world.Clock.UtcNow += TimeSpan.FromDays(5);
        var readOnly = (await world.GetSubscription.HandleAsync()).Value!;

        Assert.Equal(SubscriptionStatus.Grace, grace.Status);
        Assert.Equal(SubscriptionStatus.ReadOnly, readOnly.Status);
        Assert.True(readOnly.IsReadOnly);
    }

    // ABO-04
    [Fact]
    public async Task Declaring_a_payment_waits_for_validation_and_activates_nothing()
    {
        var world = await OwnerWorldAsync();

        var result = await world.DeclareSubscriptionPayment.HandleAsync(Declare());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyProcessed);
        var view = (await world.GetSubscription.HandleAsync()).Value!;
        Assert.Equal(SubscriptionStatus.Trial, view.Status);
        Assert.Null((await world.Commerces.GetAsync(world.Tenant, CancellationToken.None))!.PaidUntil);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.SubscriptionPaymentDeclared);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task The_amount_must_reach_the_price_of_the_period_and_the_error_says_the_price()
    {
        var world = await OwnerWorldAsync();

        var tooLow = await world.DeclareSubscriptionPayment.HandleAsync(Declare(amount: 9_999));
        var annualTooLow = await world.DeclareSubscriptionPayment.HandleAsync(Declare(SubscriptionPeriod.Annual, "W-9999", 10_000));
        var more = await world.DeclareSubscriptionPayment.HandleAsync(Declare(amount: 12_000, reference: "W-777777"));

        Assert.Equal(ErrorCodes.RuleViolation, tooLow.Error!.Code);
        Assert.Contains("10 000 FCFA", tooLow.Error.Message, StringComparison.Ordinal);
        Assert.Contains("100 000 FCFA", annualTooLow.Error!.Message, StringComparison.Ordinal);
        Assert.True(more.IsSuccess);
    }

    [Fact]
    public async Task A_reference_is_required()
    {
        var world = await OwnerWorldAsync();

        var result = await world.DeclareSubscriptionPayment.HandleAsync(Declare(reference: "  "));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Empty(world.Subscriptions.All);
    }

    [Fact]
    public async Task A_reference_serves_once_per_method_across_all_shops()
    {
        var world = await OwnerWorldAsync();
        await DeclaredAsync(world, "W-123456");
        var otherShop = new TestWorld(world.Subscriptions);
        await otherShop.GivenCommerceAsync();
        otherShop.ActAs(otherShop.Owner, Role.Owner);

        var sameMethod = await otherShop.DeclareSubscriptionPayment.HandleAsync(Declare(reference: "w123456"));
        var otherMethod = await otherShop.DeclareSubscriptionPayment.HandleAsync(Declare(reference: "W-123456", method: SubscriptionMethod.OrangeMoney));

        Assert.Equal(ErrorCodes.MobileMoneyReferenceUsed, sameMethod.Error!.Code);
        Assert.True(otherMethod.IsSuccess);
    }

    [Fact]
    public async Task Replaying_a_declaration_with_the_same_identifier_creates_nothing_more()
    {
        var world = await OwnerWorldAsync();
        var command = Declare();
        await world.DeclareSubscriptionPayment.HandleAsync(command);

        var replay = await world.DeclareSubscriptionPayment.HandleAsync(command);

        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Single(world.Subscriptions.All);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    // ABO-05
    [Fact]
    public async Task Validating_extends_the_subscription_from_the_end_of_the_trial_and_confirms_by_email()
    {
        var world = await OwnerWorldAsync();
        var trialEnd = (await world.Commerces.GetAsync(world.Tenant, CancellationToken.None))!.TrialEndsAt;
        world.Clock.UtcNow += TimeSpan.FromDays(20);
        var id = await DeclaredAsync(world);
        world.UnitOfWork.Reset();

        var result = await world.ValidatePayment.HandleAsync(id);

        Assert.True(result.IsSuccess);
        Assert.Equal(trialEnd.AddMonths(1), result.Value!.PaidUntil);
        var commerce = (await world.Commerces.GetAsync(world.Tenant, CancellationToken.None))!;
        Assert.Equal(trialEnd.AddMonths(1), commerce.PaidUntil);
        Assert.Equal(SubscriptionStatus.Active, commerce.SubscriptionStateAt(world.Clock.UtcNow).Status);
        Assert.Equal(SubscriptionPaymentStatus.Validated, (await world.Subscriptions.GetAsync(id, world.Tenant, CancellationToken.None))!.Status);
        Assert.Equal(1, world.UnitOfWork.Commits);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.SubscriptionPaymentValidated);
        var mail = Assert.Single(world.Mail.Sent);
        Assert.Equal("mamadou@exemple.ci", mail.To.Value);
        Assert.Contains("validé", mail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validating_late_starts_the_period_today_and_validating_in_advance_adds_after_the_current_end()
    {
        var world = await OwnerWorldAsync();
        world.Clock.UtcNow += TimeSpan.FromDays(50);
        var late = await DeclaredAsync(world, "W-LATE01");

        var lateResult = await world.ValidatePayment.HandleAsync(late);
        var advance = await DeclaredAsync(world, "W-ADV001", SubscriptionPeriod.Annual);
        var advanceResult = await world.ValidatePayment.HandleAsync(advance);

        Assert.Equal(world.Clock.UtcNow.AddMonths(1), lateResult.Value!.PaidUntil);
        Assert.Equal(world.Clock.UtcNow.AddMonths(13), advanceResult.Value!.PaidUntil);
    }

    [Fact]
    public async Task A_payment_cannot_be_validated_twice_or_after_a_refusal_and_an_unknown_one_is_not_found()
    {
        var world = await OwnerWorldAsync();
        var id = await DeclaredAsync(world);
        await world.ValidatePayment.HandleAsync(id);
        var refused = await DeclaredAsync(world, "W-OTHER1");
        await world.RefusePayment.HandleAsync(new RefuseSubscriptionPaymentCommand(refused, "Introuvable."));

        var again = await world.ValidatePayment.HandleAsync(id);
        var afterRefusal = await world.ValidatePayment.HandleAsync(refused);
        var unknown = await world.ValidatePayment.HandleAsync(SubscriptionPaymentId.New());

        Assert.Equal(ErrorCodes.RuleViolation, again.Error!.Code);
        Assert.Equal(ErrorCodes.RuleViolation, afterRefusal.Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, unknown.Error!.Code);
        Assert.Equal(1, world.Commerces.All.Count(commerce => commerce.PaidUntil is not null));
    }

    [Fact]
    public async Task Refusing_needs_a_reason_keeps_the_dates_and_tells_the_owner()
    {
        var world = await OwnerWorldAsync();
        var id = await DeclaredAsync(world);

        var noReason = await world.RefusePayment.HandleAsync(new RefuseSubscriptionPaymentCommand(id, " "));
        var result = await world.RefusePayment.HandleAsync(new RefuseSubscriptionPaymentCommand(id, "Référence introuvable sur le relevé."));

        Assert.Equal(ErrorCodes.RuleViolation, noReason.Error!.Code);
        Assert.True(result.IsSuccess);
        Assert.Null((await world.Commerces.GetAsync(world.Tenant, CancellationToken.None))!.PaidUntil);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.SubscriptionPaymentRefused);
        Assert.Contains("Référence introuvable sur le relevé.", Assert.Single(world.Mail.Sent).Message, StringComparison.Ordinal);
        var view = (await world.GetSubscription.HandleAsync()).Value!;
        Assert.Equal("Référence introuvable sur le relevé.", view.Payments![0].Reason);
    }

    [Fact]
    public async Task A_failing_mail_server_does_not_undo_a_validation()
    {
        var world = await OwnerWorldAsync();
        var id = await DeclaredAsync(world);
        world.Mail.Failing = true;

        var result = await world.ValidatePayment.HandleAsync(id);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.EmailSent);
        Assert.NotNull((await world.Commerces.GetAsync(world.Tenant, CancellationToken.None))!.PaidUntil);
    }

    [Fact]
    public async Task The_pending_list_names_the_shop_the_method_the_reference_and_the_amount()
    {
        var world = await OwnerWorldAsync();
        await DeclaredAsync(world);
        var done = await DeclaredAsync(world, "W-DONE01");
        await world.ValidatePayment.HandleAsync(done);

        var pending = await world.ListPendingPayments.HandleAsync();

        var line = Assert.Single(pending);
        Assert.Equal("Supérette du Carrefour", line.CommerceName);
        Assert.Equal("W-123456", line.Reference);
        Assert.Equal(10_000, line.AmountFcfa);
        Assert.Equal(SubscriptionMethod.Wave, line.Method);
    }

    // ABO-06
    [Fact]
    public async Task Reminders_go_out_at_seven_three_and_zero_days_once_each()
    {
        var world = await OwnerWorldAsync();
        var trialEnd = (await world.Commerces.GetAsync(world.Tenant, CancellationToken.None))!.TrialEndsAt;

        world.Clock.UtcNow = trialEnd.AddDays(-8);
        var tooEarly = await world.SendReminders.HandleAsync();
        world.Clock.UtcNow = trialEnd.AddDays(-6.5);
        var sevenDays = await world.SendReminders.HandleAsync();
        var again = await world.SendReminders.HandleAsync();
        world.Clock.UtcNow = trialEnd.AddDays(-2.5);
        var threeDays = await world.SendReminders.HandleAsync();
        world.Clock.UtcNow = trialEnd.AddHours(-2);
        var lastDay = await world.SendReminders.HandleAsync();

        Assert.Equal(0, tooEarly.Value);
        Assert.Equal(1, sevenDays.Value);
        Assert.Equal(0, again.Value);
        Assert.Equal(1, threeDays.Value);
        Assert.Equal(1, lastDay.Value);
        Assert.Equal(3, world.Mail.Sent.Count);
        Assert.Contains("7 jours", world.Mail.Sent[0].Message, StringComparison.Ordinal);
        Assert.Contains("10 000 FCFA", world.Mail.Sent[0].Message, StringComparison.Ordinal);
        Assert.Contains("07 00 00 00 01", world.Mail.Sent[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missed_day_does_not_lose_the_reminder_and_a_payment_stops_the_next_ones()
    {
        var world = await OwnerWorldAsync();
        var trialEnd = (await world.Commerces.GetAsync(world.Tenant, CancellationToken.None))!.TrialEndsAt;
        world.Clock.UtcNow = trialEnd.AddDays(-1.5);

        var late = await world.SendReminders.HandleAsync();
        var id = await DeclaredAsync(world);
        await world.ValidatePayment.HandleAsync(id);
        world.Mail.Clear();
        world.Clock.UtcNow = trialEnd.AddHours(-1);
        var afterPayment = await world.SendReminders.HandleAsync();

        Assert.Equal(1, late.Value);
        Assert.Equal(0, afterPayment.Value);
        Assert.Empty(world.Mail.Sent);
    }

    [Fact]
    public async Task One_last_email_says_the_shop_is_read_only_and_nothing_is_lost()
    {
        var world = await OwnerWorldAsync();
        var trialEnd = (await world.Commerces.GetAsync(world.Tenant, CancellationToken.None))!.TrialEndsAt;
        world.Clock.UtcNow = trialEnd.AddDays(8);

        var first = await world.SendReminders.HandleAsync();
        var second = await world.SendReminders.HandleAsync();

        Assert.Equal(1, first.Value);
        Assert.Equal(0, second.Value);
        Assert.Contains("lecture seule", world.Mail.Sent[^1].Message, StringComparison.Ordinal);
        Assert.Contains("conserv", world.Mail.Sent[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failing_mail_server_is_retried_next_time_instead_of_being_marked_sent()
    {
        var world = await OwnerWorldAsync();
        var trialEnd = (await world.Commerces.GetAsync(world.Tenant, CancellationToken.None))!.TrialEndsAt;
        world.Clock.UtcNow = trialEnd.AddDays(-2.5);
        world.Mail.Failing = true;
        var failed = await world.SendReminders.HandleAsync();
        world.Mail.Failing = false;

        var retried = await world.SendReminders.HandleAsync();

        Assert.Equal(0, failed.Value);
        Assert.Equal(1, retried.Value);
    }

    // ABO-07
    [Fact]
    public async Task The_guard_state_of_a_shop_is_read_only_after_the_grace()
    {
        var world = await OwnerWorldAsync();

        var trial = await world.SubscriptionState.HandleAsync(world.Tenant);
        world.Clock.UtcNow += TimeSpan.FromDays(40);
        var expired = await world.SubscriptionState.HandleAsync(world.Tenant);
        var unknown = await world.SubscriptionState.HandleAsync(TenantId.New());

        Assert.Equal(SubscriptionStatus.Trial, trial!.Status);
        Assert.True(expired!.IsReadOnly);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task A_sync_received_in_read_only_is_recorded_in_the_journal()
    {
        var world = await OwnerWorldAsync();
        world.ActAs(world.Cashier, Role.Cashier);

        await world.RecordExpiredSync.HandleAsync("/api/v1/sales/sync");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.SubscriptionExpiredSale, entry.Action);
        Assert.Contains("/api/v1/sales/sync", entry.Detail, StringComparison.Ordinal);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }
}
