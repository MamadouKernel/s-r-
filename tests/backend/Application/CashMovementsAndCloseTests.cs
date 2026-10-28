using Soro.Application.Cash;
using Soro.Application.Common;
using Soro.Application.Sales;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

/// <summary>Sorties d'espèces et clôture à l'aveugle (docs/specs/cloture-sorties-rapport, lot 1).</summary>
public class CashMovementsAndCloseTests
{
    private static RecordCashMovementCommand Expense(CashRegisterSession session, long amount = 1_500, string? reason = "Transport", CashMovementId? id = null, Approval? approval = null) =>
        new(session.Id, id ?? CashMovementId.New(), CashMovementKind.Expense, amount, reason, TestWorld.Morning.AddHours(3), approval);

    private static CloseRegisterSessionCommand Close(CashRegisterSession session, long counted, bool recount = false, IReadOnlyDictionary<int, int>? detail = null) =>
        new(session.Id, counted, TestWorld.Morning.AddHours(9), recount, detail);

    // ---- Mouvements --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_cashier_records_a_small_expense_on_his_own_session_and_the_expected_cash_follows()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 10_000);

        var result = await world.RecordCashMovement.HandleAsync(Expense(session));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyProcessed);
        Assert.Equal(Money.Fcfa(8_500), session.ExpectedCash);
        var stored = Assert.Single(world.CashMovements.All);
        Assert.Equal((CashMovementKind.Expense, Money.Fcfa(1_500), "Transport"), (stored.Kind, stored.Amount, stored.Reason));
        Assert.Equal(world.Cashier, stored.By);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.CashMovement);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task The_same_movement_sent_twice_is_recorded_once()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var id = CashMovementId.New();

        await world.RecordCashMovement.HandleAsync(Expense(session, id: id));
        var again = await world.RecordCashMovement.HandleAsync(Expense(session, id: id));

        Assert.True(again.Value!.AlreadyProcessed);
        Assert.Single(world.CashMovements.All);
        Assert.Equal(Money.Fcfa(8_500), session.ExpectedCash);
    }

    [Fact]
    public async Task A_big_expense_or_drop_needs_a_manager_and_a_small_one_or_a_topup_does_not()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 50_000);

        var bigExpense = await world.RecordCashMovement.HandleAsync(Expense(session, 5_001));
        var bigDrop = await world.RecordCashMovement.HandleAsync(new(session.Id, CashMovementId.New(), CashMovementKind.Drop, 20_000, null, TestWorld.Morning.AddHours(3), null));
        var edge = await world.RecordCashMovement.HandleAsync(Expense(session, 5_000));
        var topup = await world.RecordCashMovement.HandleAsync(new(session.Id, CashMovementId.New(), CashMovementKind.Topup, 30_000, null, TestWorld.Morning.AddHours(3), null));

        Assert.Equal(ErrorCodes.Forbidden, bigExpense.Error!.Code);
        Assert.Equal(ErrorCodes.Forbidden, bigDrop.Error!.Code);
        Assert.True(edge.IsSuccess);
        Assert.True(topup.IsSuccess);
        Assert.Equal(2, world.Audit.Entries.Count(entry => entry.Action == AuditActions.CashMovementRefused));
        Assert.Equal(2, world.CashMovements.All.Count);
    }

    [Fact]
    public async Task A_manager_approval_covers_the_movement_and_the_amount_it_was_given_for_and_the_journal_names_the_manager()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 50_000);
        var manager = world.GivenUser("gerant", pin: "4321", role: Role.Manager);
        var id = CashMovementId.New();
        var small = (await world.GrantApproval.HandleAsync(new GrantApprovalCommand("gerant", "4321", id.Value, ApprovalPurposes.CashOut, 8_000))).Value!;

        var tooBig = await world.RecordCashMovement.HandleAsync(Expense(session, 9_000, id: id, approval: Approval.FromToken(small.Token)));
        var covered = await world.RecordCashMovement.HandleAsync(Expense(session, 8_000, id: id, approval: Approval.FromToken(small.Token)));

        Assert.False(tooBig.IsSuccess);
        Assert.True(covered.IsSuccess);
        Assert.Equal(manager.Id, Assert.Single(world.CashMovements.All).ApprovedBy);
    }

    [Fact]
    public async Task A_manager_needs_nobody_to_validate_his_own_big_drop()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(cashier: world.Manager, openingFloat: 50_000);
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.RecordCashMovement.HandleAsync(new(session.Id, CashMovementId.New(), CashMovementKind.Drop, 30_000, null, TestWorld.Morning.AddHours(3), null));

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(world.CashMovements.All).ApprovedBy);
    }

    [Fact]
    public async Task An_expense_needs_a_reason_and_cannot_empty_more_than_the_drawer_holds()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 1_000);

        var noReason = await world.RecordCashMovement.HandleAsync(Expense(session, 500, reason: null));
        var tooMuch = await world.RecordCashMovement.HandleAsync(Expense(session, 1_001));

        Assert.Equal(ErrorCodes.RuleViolation, noReason.Error!.Code);
        Assert.Equal(ErrorCodes.RuleViolation, tooMuch.Error!.Code);
        Assert.Empty(world.CashMovements.All);
    }

    [Fact]
    public async Task Only_the_holder_of_the_session_or_a_manager_may_record_and_a_closed_or_foreign_session_is_refused()
    {
        var world = new TestWorld();
        var other = world.GivenUser("collegue", role: Role.Cashier);
        var session = world.GivenOpenSession(cashier: other.Id);
        var foreign = world.GivenOpenSession(tenant: TenantId.New());

        var notMine = await world.RecordCashMovement.HandleAsync(Expense(session));
        var unknown = await world.RecordCashMovement.HandleAsync(Expense(foreign));
        world.ActAs(world.Manager, Role.Manager);
        var byManager = await world.RecordCashMovement.HandleAsync(Expense(session));
        session.Close(session.ExpectedCash, TestWorld.Morning.AddHours(9));
        var closed = await world.RecordCashMovement.HandleAsync(Expense(session));

        Assert.Equal(ErrorCodes.Forbidden, notMine.Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, unknown.Error!.Code);
        Assert.True(byManager.IsSuccess);
        Assert.Equal(ErrorCodes.RuleViolation, closed.Error!.Code);
    }

    // ---- Clôture à l'aveugle -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_cashier_who_counts_right_closes_and_is_told_neither_the_expected_amount_nor_the_variance()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 10_000);

        var result = await world.CloseSession.HandleAsync(Close(session, 9_800));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Closed);
        Assert.False(result.Value.NeedsRecount);
        Assert.Equal(9_800, result.Value.CountedCashFcfa);
        Assert.Null(result.Value.ExpectedCashFcfa);
        Assert.Null(result.Value.VarianceFcfa);
        Assert.Equal(SessionStatus.Closed, session.Status);
        Assert.DoesNotContain(world.Audit.Entries, entry => entry.Action == AuditActions.SessionVariance);
    }

    [Fact]
    public async Task A_count_beyond_the_tolerance_asks_for_a_recount_without_a_word_on_the_amount()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 10_000);

        var result = await world.CloseSession.HandleAsync(Close(session, 12_000));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.NeedsRecount);
        Assert.False(result.Value.Closed);
        Assert.Null(result.Value.ExpectedCashFcfa);
        Assert.Null(result.Value.VarianceFcfa);
        Assert.Equal(SessionStatus.Open, session.Status);
        Assert.Equal(Money.Fcfa(12_000), session.FirstCount);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task The_recount_is_final_and_a_real_variance_goes_to_the_journal_under_the_cashier_whatever_its_direction()
    {
        var world = new TestWorld();
        var shortage = world.GivenOpenSession(openingFloat: 10_000);
        await world.CloseSession.HandleAsync(Close(shortage, 8_000));

        var closed = await world.CloseSession.HandleAsync(Close(shortage, 8_000, recount: true));

        Assert.True(closed.Value!.Closed);
        Assert.Equal(-2_000, shortage.CashVariance);
        var entry = Assert.Single(world.Audit.Entries, item => item.Action == AuditActions.SessionVariance);
        Assert.Equal(world.Cashier, entry.User);
        Assert.Contains("manque de 2 000", entry.Detail.Replace(' ', ' '), StringComparison.Ordinal);

        var surplus = world.GivenOpenSession(openingFloat: 10_000);
        await world.CloseSession.HandleAsync(Close(surplus, 12_000));
        await world.CloseSession.HandleAsync(Close(surplus, 12_000, recount: true));
        Assert.Contains(world.Audit.Entries, item => item.Action == AuditActions.SessionVariance && item.Detail.Contains("excédent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_manager_who_closes_sees_the_expected_amount_and_the_variance()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 10_000);
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.CloseSession.HandleAsync(Close(session, 9_900));

        Assert.Equal(10_000, result.Value!.ExpectedCashFcfa);
        Assert.Equal(-100, result.Value.VarianceFcfa);
    }

    [Fact]
    public async Task An_expense_is_not_a_shortage_and_a_replay_of_the_closing_changes_nothing()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 10_000);
        await world.RecordCashMovement.HandleAsync(Expense(session, 1_500));

        var first = await world.CloseSession.HandleAsync(Close(session, 8_500));
        var replay = await world.CloseSession.HandleAsync(Close(session, 8_500));
        var other = await world.CloseSession.HandleAsync(Close(session, 8_000));

        Assert.Equal(0, session.CashVariance);
        Assert.True(first.Value!.Closed);
        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Equal(ErrorCodes.RuleViolation, other.Error!.Code);
        Assert.DoesNotContain(world.Audit.Entries, entry => entry.Action == AuditActions.SessionVariance);
    }

    [Fact]
    public async Task The_same_first_count_sent_again_asks_again_for_the_recount_and_keeps_the_first_one()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 10_000);
        await world.CloseSession.HandleAsync(Close(session, 12_000));

        var again = await world.CloseSession.HandleAsync(Close(session, 12_000));

        Assert.True(again.Value!.NeedsRecount);
        Assert.Equal(Money.Fcfa(12_000), session.FirstCount);
    }

    [Fact]
    public async Task The_denominations_must_add_up_to_the_count_and_are_kept()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession(openingFloat: 10_000);

        var wrong = await world.CloseSession.HandleAsync(Close(session, 10_000, detail: new Dictionary<int, int> { [5_000] = 1, [1_000] = 4 }));
        var right = await world.CloseSession.HandleAsync(Close(session, 10_000, detail: new Dictionary<int, int> { [5_000] = 1, [1_000] = 5 }));

        Assert.Equal(ErrorCodes.RuleViolation, wrong.Error!.Code);
        Assert.True(right.Value!.Closed);
        Assert.Contains("5000", session.CountDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stock_keeper_cannot_close_and_the_settings_hold_the_starting_values()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        world.ActAs(world.Cashier, Role.StockKeeper);

        var refused = await world.CloseSession.HandleAsync(Close(session, 10_000));

        Assert.Equal(ErrorCodes.Forbidden, refused.Error!.Code);
        var settings = Soro.Application.Settings.ShopSettings.Default;
        Assert.Equal((500L, 5_000L, 14, 100_000L), (settings.CashVarianceToleranceFcfa, settings.CashOutApprovalThresholdFcfa, settings.OpenSessionAlertHours, settings.DrawerWarningFcfa));
    }
}
