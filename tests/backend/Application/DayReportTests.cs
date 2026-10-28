using Soro.Application.Common;
using Soro.Application.Reporting;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Sales;

namespace Soro.Application.Tests;

/// <summary>Rapport du jour et e-mail du soir (docs/specs/cloture-sorties-rapport CLO-06 à CLO-08, CLO-11).</summary>
public class DayReportTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);

    private static DayFigures Figures(TestWorld world, params SessionFigures[] sessions) => new(
        SalesCount: 4,
        NetTotalFcfa: 20_000,
        Payments: [new PaymentBreakdown(PaymentMethod.Cash, 12_000, 3), new PaymentBreakdown(PaymentMethod.Wave, 8_000, 1)],
        Cashiers: [new CashierFigures(world.Cashier.Value, 4, 20_000, 1, 500, 1, 2_000, 1, 1_000)],
        Movements: [new CashMovementTotal(CashMovementKind.Expense, "Transport", 2, 3_000), new CashMovementTotal(CashMovementKind.Drop, null, 1, 40_000)],
        Sessions: sessions,
        DebtCreatedFcfa: 1_500,
        RepaymentsFcfa: 700,
        LowStock: [new LowStockLine("Jus d'orange 1L", 2, 5, MeasureUnit.Piece)]);

    private static SessionFigures Session(TestWorld world, SessionStatus status = SessionStatus.Closed, DateTimeOffset? opened = null, long expected = 10_000, long? counted = 10_000, long? variance = 0) =>
        new(Guid.NewGuid(), world.Cashier.Value, opened ?? TestWorld.Morning, status == SessionStatus.Closed ? TestWorld.Morning.AddHours(9) : null, status, 10_000, expected, counted, variance);

    [Fact]
    public async Task The_owner_reads_the_day_with_the_average_basket_the_payments_the_cashiers_the_outflows_and_the_stock()
    {
        var world = await OwnerWorldAsync();
        world.Reporting.Day = Figures(world, Session(world));

        var result = await world.DayReport.HandleAsync(new GetDayReportQuery(Day));

        Assert.True(result.IsSuccess);
        var report = result.Value!;
        Assert.Equal(5_000, report.AverageBasketFcfa);
        Assert.Equal(20_000, report.NetTotalFcfa);
        Assert.Equal(2, report.Payments.Count);
        Assert.Equal(500, report.Cashiers.Single().DiscountFcfa);
        Assert.Equal(43_000, report.Movements.Sum(item => item.AmountFcfa));
        Assert.Single(report.LowStock);
        Assert.Equal(Day, report.Day);
    }

    [Fact]
    public async Task An_accountant_reads_the_report_without_the_stock_and_a_cashier_cannot_read_it()
    {
        var world = await OwnerWorldAsync();
        world.Reporting.Day = Figures(world, Session(world));
        world.ActAs(world.Owner, Role.Accountant);
        var accountant = await world.DayReport.HandleAsync(new GetDayReportQuery(Day));
        world.ActAs(world.Cashier, Role.Cashier);
        var cashier = await world.DayReport.HandleAsync(new GetDayReportQuery(Day));

        Assert.Empty(accountant.Value!.LowStock);
        Assert.Equal(ErrorCodes.Forbidden, cashier.Error!.Code);
    }

    [Fact]
    public async Task A_session_open_for_more_than_fourteen_hours_is_flagged_to_close_and_a_drawer_over_the_warning_is_flagged()
    {
        var world = await OwnerWorldAsync();
        world.Clock.UtcNow = TestWorld.Morning.AddHours(15);
        world.Reporting.Day = Figures(
            world,
            Session(world, SessionStatus.Open, opened: TestWorld.Morning, expected: 120_000, counted: null, variance: null),
            Session(world, SessionStatus.Open, opened: TestWorld.Morning.AddHours(10), expected: 20_000, counted: null, variance: null));

        var report = (await world.DayReport.HandleAsync(new GetDayReportQuery(Day))).Value!;

        Assert.True(report.Sessions[0].NeedsClosing);
        Assert.True(report.Sessions[0].DrawerAboveWarning);
        Assert.False(report.Sessions[1].NeedsClosing);
        Assert.False(report.Sessions[1].DrawerAboveWarning);
        Assert.Equal(report.Sessions.Where(session => session.NeedsClosing).Select(session => session.Id), report.Sessions.Take(1).Select(session => session.Id));
    }

    [Fact]
    public async Task The_variances_beyond_the_tolerance_are_counted_per_cashier_in_both_directions_and_never_turned_into_a_deduction()
    {
        var world = await OwnerWorldAsync();
        world.Reporting.Day = Figures(world, Session(world, variance: -2_000), Session(world, variance: 300), Session(world, variance: 1_200));

        var report = (await world.DayReport.HandleAsync(new GetDayReportQuery(Day))).Value!;

        var cashier = report.Cashiers.Single();
        Assert.Equal(-500, cashier.VarianceFcfa);
        Assert.Equal(2, cashier.VarianceSessions);
        Assert.DoesNotContain(typeof(DayReport).GetProperties(), property => property.Name.Contains("Deduction", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_evening_mail_goes_once_per_shop_and_per_day_with_the_figures_in_plain_french()
    {
        var world = await OwnerWorldAsync();
        var awa = world.GivenUser("awa", role: Role.Cashier);
        world.Reporting.Day = Figures(world, Session(world, variance: -2_000)) with { Cashiers = [new CashierFigures(awa.Id.Value, 4, 20_000, 1, 500, 1, 2_000, 1, 1_000)] };
        world.Clock.UtcNow = TestWorld.Morning.AddHours(12);

        var first = await world.SendEveningReports.HandleAsync(Day);
        var again = await world.SendEveningReports.HandleAsync(Day);

        Assert.Equal(1, first.Value);
        Assert.Equal(0, again.Value);
        var mail = Assert.Single(world.Mail.Sent);
        var text = mail.Message.Replace(' ', ' ');
        Assert.Contains("20 000 FCFA", text, StringComparison.Ordinal);
        Assert.Contains("Awa Koné", text, StringComparison.Ordinal);
        Assert.Contains("Transport", text, StringComparison.Ordinal);
        Assert.Contains("Wave", text, StringComparison.Ordinal);
        Assert.Contains("manque", text, StringComparison.Ordinal);
        Assert.Contains("Jus d'orange 1L", text, StringComparison.Ordinal);
        Assert.DoesNotContain("variance", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_mail_for_a_day_without_any_activity_or_for_a_shop_in_read_only()
    {
        var world = await OwnerWorldAsync();
        world.Reporting.Day = new DayFigures(0, 0, [], [], [], [], 0, 0, []);
        var quiet = await world.SendEveningReports.HandleAsync(Day);

        var busy = await OwnerWorldAsync();
        busy.Reporting.Day = Figures(busy, Session(busy));
        var trialEnd = (await busy.Commerces.GetAsync(busy.Tenant, CancellationToken.None))!.TrialEndsAt;
        busy.Clock.UtcNow = trialEnd.AddDays(60);
        var readOnly = await busy.SendEveningReports.HandleAsync(Day);

        Assert.Equal(0, quiet.Value);
        Assert.Empty(world.Mail.Sent);
        Assert.Equal(0, readOnly.Value);
        Assert.Empty(busy.Mail.Sent);
    }

    [Fact]
    public async Task A_day_with_only_an_open_session_still_sends_the_mail_and_a_mail_server_failure_is_retried_next_time()
    {
        var world = await OwnerWorldAsync();
        world.Reporting.Day = new DayFigures(0, 0, [], [], [], [Session(world, SessionStatus.Open, expected: 10_000, counted: null, variance: null)], 0, 0, []);
        world.Mail.Failing = true;
        var failed = await world.SendEveningReports.HandleAsync(Day);
        world.Mail.Failing = false;
        var retried = await world.SendEveningReports.HandleAsync(Day);

        Assert.Equal(0, failed.Value);
        Assert.Equal(1, retried.Value);
        Assert.Single(world.Mail.Sent);
    }

    private static async Task<TestWorld> OwnerWorldAsync()
    {
        var world = new TestWorld();
        await world.GivenCommerceAsync();
        world.ActAs(world.Owner, Role.Owner);
        return world;
    }
}
