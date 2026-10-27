using Soro.Application.Common;
using Soro.Application.Sales;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Sales;

namespace Soro.Application.Tests;

public class ApprovalGrantTests
{
    private static GrantApprovalCommand Ask(string identifier, string pin, Guid? subject = null, string purpose = ApprovalPurposes.Reversal) =>
        new(identifier, pin, subject ?? Guid.NewGuid(), purpose);

    [Fact]
    public async Task A_manager_identifier_and_pin_give_a_signed_approval_for_one_subject()
    {
        var world = new TestWorld();
        var manager = world.GivenUser("gerant", pin: "4321", role: Role.Manager);
        var subject = Guid.NewGuid();

        var result = await world.GrantApproval.HandleAsync(Ask(" GERANT ", "4321", subject));

        Assert.True(result.IsSuccess);
        Assert.Equal(manager.Id, result.Value!.Approver);
        Assert.Equal("Awa Koné", result.Value.ApproverName);
        Assert.False(string.IsNullOrEmpty(result.Value.Token));
        Assert.Equal(manager.Id, world.Seal.Open(result.Value.Token, world.Tenant, subject, ApprovalPurposes.Reversal, world.Clock.UtcNow));
    }

    [Fact]
    public async Task A_discount_approval_lasts_three_days_a_reversal_approval_fifteen_minutes()
    {
        var world = new TestWorld();
        world.GivenUser("gerant", pin: "4321", role: Role.Manager);

        var discount = await world.GrantApproval.HandleAsync(Ask("gerant", "4321", purpose: ApprovalPurposes.Discount));
        var reversal = await world.GrantApproval.HandleAsync(Ask("gerant", "4321", purpose: ApprovalPurposes.Reversal));

        Assert.Equal(world.Clock.UtcNow.AddDays(3), discount.Value!.ExpiresAt);
        Assert.Equal(world.Clock.UtcNow.AddMinutes(15), reversal.Value!.ExpiresAt);
    }

    [Fact]
    public async Task A_wrong_pin_an_unknown_account_a_cashier_and_a_disabled_manager_all_get_the_same_refusal()
    {
        var world = new TestWorld();
        world.GivenUser("gerant", pin: "4321", role: Role.Manager);
        world.GivenUser("caissier2", pin: "1111", role: Role.Cashier);
        var gone = world.GivenUser("parti", pin: "2222", role: Role.Manager);
        gone.Disable(world.Clock.UtcNow);

        var answers = new[]
        {
            await world.GrantApproval.HandleAsync(Ask("gerant", "0000")),
            await world.GrantApproval.HandleAsync(Ask("fantome", "4321")),
            await world.GrantApproval.HandleAsync(Ask("caissier2", "1111")),
            await world.GrantApproval.HandleAsync(Ask("parti", "2222")),
        };

        Assert.All(answers, answer =>
        {
            Assert.False(answer.IsSuccess);
            Assert.Equal(ErrorCodes.InvalidCredentials, answer.Error!.Code);
            Assert.Equal("Identifiant ou code PIN incorrect.", answer.Error.Message);
        });
        Assert.Equal(4, world.Audit.Entries.Count(entry => entry.Action == AuditActions.ApprovalRefused));
    }

    [Fact]
    public async Task Five_wrong_pins_lock_the_manager_so_that_even_the_right_pin_is_refused()
    {
        var world = new TestWorld();
        world.GivenUser("gerant", pin: "4321", role: Role.Manager);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await world.GrantApproval.HandleAsync(Ask("gerant", "0000"));
        }

        var right = await world.GrantApproval.HandleAsync(Ask("gerant", "4321"));

        Assert.False(right.IsSuccess);
        Assert.Equal(ErrorCodes.InvalidCredentials, right.Error!.Code);
    }

    [Fact]
    public async Task Only_a_profile_that_works_at_the_till_can_ask_and_the_purpose_must_be_known()
    {
        var world = new TestWorld();
        world.GivenUser("gerant", pin: "4321", role: Role.Manager);

        var unknown = await world.GrantApproval.HandleAsync(Ask("gerant", "4321", purpose: "autre"));
        world.ActAs(world.Cashier, Role.StockKeeper);
        var stockKeeper = await world.GrantApproval.HandleAsync(Ask("gerant", "4321"));

        Assert.Equal(ErrorCodes.RuleViolation, unknown.Error!.Code);
        Assert.Equal(ErrorCodes.Forbidden, stockKeeper.Error!.Code);
    }

    [Fact]
    public async Task The_approval_authorizes_the_cancellation_of_that_sale_and_the_journal_names_the_manager()
    {
        var world = new TestWorld();
        var product = world.GivenProduct(stock: 10);
        var session = world.GivenOpenSession();
        var sale = world.GivenSale(session, product, quantity: 1, price: 1_000);
        var manager = world.GivenUser("gerant", pin: "4321", role: Role.Manager);
        var approval = (await world.GrantApproval.HandleAsync(Ask("gerant", "4321", sale.Id.Value))).Value!;

        var result = await world.CancelSale.HandleAsync(new CancelSaleCommand(sale.Id, "Erreur de saisie", null, null, true, Approval.FromToken(approval.Token)));

        Assert.True(result.IsSuccess);
        Assert.Equal(manager.Id, Assert.Single(await world.Returns.ListForSaleAsync(sale.Id, world.Tenant, CancellationToken.None)).ApprovedBy);
    }

    [Fact]
    public async Task An_approval_for_another_sale_another_purpose_or_after_its_time_is_refused()
    {
        var world = new TestWorld();
        var product = world.GivenProduct(stock: 10);
        var session = world.GivenOpenSession();
        var sale = world.GivenSale(session, product, quantity: 1, price: 1_000);
        world.GivenUser("gerant", pin: "4321", role: Role.Manager);
        var other = (await world.GrantApproval.HandleAsync(Ask("gerant", "4321", Guid.NewGuid()))).Value!;
        var discount = (await world.GrantApproval.HandleAsync(Ask("gerant", "4321", sale.Id.Value, ApprovalPurposes.Discount))).Value!;
        var late = (await world.GrantApproval.HandleAsync(Ask("gerant", "4321", sale.Id.Value))).Value!;
        world.Clock.UtcNow = world.Clock.UtcNow.AddMinutes(16);

        foreach (var token in new[] { other.Token, discount.Token, late.Token, "n'importe quoi" })
        {
            var result = await world.CancelSale.HandleAsync(new CancelSaleCommand(sale.Id, "Erreur", null, null, true, Approval.FromToken(token)));
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        }
    }

    [Fact]
    public async Task A_manager_disabled_after_giving_the_approval_no_longer_counts()
    {
        var world = new TestWorld();
        var product = world.GivenProduct(stock: 10);
        var session = world.GivenOpenSession();
        var sale = world.GivenSale(session, product, quantity: 1, price: 1_000);
        var manager = world.GivenUser("gerant", pin: "4321", role: Role.Manager);
        var approval = (await world.GrantApproval.HandleAsync(Ask("gerant", "4321", sale.Id.Value))).Value!;
        manager.Disable(world.Clock.UtcNow);

        var result = await world.CancelSale.HandleAsync(new CancelSaleCommand(sale.Id, "Erreur", null, null, true, Approval.FromToken(approval.Token)));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task A_discount_approval_lets_a_large_discount_through_even_from_the_offline_queue()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        world.GivenUser("gerant", pin: "4321", role: Role.Manager);
        var draft = world.CashSale(session, price: 1_000, discount: 200);
        var approval = (await world.GrantApproval.HandleAsync(Ask("gerant", "4321", draft.Id.Value, ApprovalPurposes.Discount))).Value!;

        var result = await world.RecordSale.HandleAsync((draft with { DiscountApproval = Approval.FromToken(approval.Token) }) with { FromOfflineQueue = true });

        Assert.True(result.IsSuccess);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.DiscountApproved);
    }
}
