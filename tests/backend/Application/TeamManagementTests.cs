using Soro.Application.Common;
using Soro.Application.Devices;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

/// <summary>Gestion complète de l'équipe (docs/specs/equipe-gestion-complete) : EQP-01 à EQP-06.</summary>
public class ResetUserPinTests
{
    private static (TestWorld World, User Target) AsOwnerWithACashier()
    {
        var world = new TestWorld();
        var target = world.GivenUser(role: Role.Cashier);
        world.ActAs(world.Owner, Role.Owner);
        return (world, target);
    }

    [Fact]
    public async Task The_owner_gives_a_cashier_a_new_pin_and_it_is_audited_without_the_pin()
    {
        var (world, target) = AsOwnerWithACashier();

        var result = await world.ResetUserPin.HandleAsync(new ResetUserPinCommand(target.Id, "987654"));

        Assert.True(result.IsSuccess);
        Assert.Equal("hash:987654", target.PinHash);
        Assert.Equal(world.Clock.UtcNow, target.SessionsValidFrom);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.UserPinReset, entry.Action);
        Assert.Equal(world.Owner, entry.User);
        Assert.Equal(target.Id.Value, entry.ResourceId);
        Assert.DoesNotContain("987654", entry.Detail);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_locked_cashier_can_log_in_again_with_the_new_pin()
    {
        var (world, target) = AsOwnerWithACashier();
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            target.RecordFailedAttempt(world.Clock.UtcNow);
        }

        await world.ResetUserPin.HandleAsync(new ResetUserPinCommand(target.Id, "5555"));
        var login = await world.LogIn.HandleAsync(new LogInCommand(world.Tenant, target.Identifier, "5555"));

        Assert.True(login.IsSuccess);
    }

    [Fact]
    public async Task The_old_pin_stops_working()
    {
        var (world, target) = AsOwnerWithACashier();

        await world.ResetUserPin.HandleAsync(new ResetUserPinCommand(target.Id, "5555"));
        var login = await world.LogIn.HandleAsync(new LogInCommand(world.Tenant, target.Identifier, "1234"));

        Assert.Equal(ErrorCodes.InvalidCredentials, login.Error!.Code);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("12a4")]
    [InlineData("")]
    public async Task Refuses_a_pin_that_is_not_4_to_6_digits(string pin)
    {
        var (world, target) = AsOwnerWithACashier();

        var result = await world.ResetUserPin.HandleAsync(new ResetUserPinCommand(target.Id, pin));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Equal("hash:1234", target.PinHash);
        Assert.Empty(world.Audit.Entries);
    }

    [Fact]
    public async Task A_manager_gives_a_cashier_a_new_pin_but_not_a_manager()
    {
        var world = new TestWorld();
        var cashier = world.GivenUser("caissier", role: Role.Cashier);
        var otherManager = world.GivenUser("gerant2", role: Role.Manager);
        world.ActAs(world.Manager, Role.Manager);

        var allowed = await world.ResetUserPin.HandleAsync(new ResetUserPinCommand(cashier.Id, "5555"));
        var refused = await world.ResetUserPin.HandleAsync(new ResetUserPinCommand(otherManager.Id, "5555"));

        Assert.True(allowed.IsSuccess);
        Assert.Equal(ErrorCodes.Forbidden, refused.Error!.Code);
        Assert.Equal("hash:1234", otherManager.PinHash);
    }

    [Fact]
    public async Task Nobody_changes_their_own_pin_this_way()
    {
        var world = new TestWorld();
        var self = world.GivenUser("moi", role: Role.Cashier);
        world.ActAs(self.Id, Role.Owner);

        var result = await world.ResetUserPin.HandleAsync(new ResetUserPinCommand(self.Id, "5555"));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Equal("hash:1234", self.PinHash);
    }

    [Fact]
    public async Task The_owner_has_no_pin()
    {
        var world = new TestWorld();
        var owner = User.RegisterOwner(
            UserId.New(), world.Tenant, EmailAddress.Parse("mamadou@exemple.ci"), "Mamadou", "password-hash", world.Clock.UtcNow);
        world.Users.Put(owner);
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.ResetUserPin.HandleAsync(new ResetUserPinCommand(owner.Id, "5555"));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task A_disabled_member_must_be_reactivated_first()
    {
        var (world, target) = AsOwnerWithACashier();
        target.Disable(world.Clock.UtcNow);

        var result = await world.ResetUserPin.HandleAsync(new ResetUserPinCommand(target.Id, "5555"));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("réactiv", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Other_profiles_cannot_reset_a_pin(Role actor)
    {
        var world = new TestWorld();
        var target = world.GivenUser(role: Role.Cashier);
        world.ActAs(UserId.New(), actor);

        var result = await world.ResetUserPin.HandleAsync(new ResetUserPinCommand(target.Id, "5555"));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task Fails_for_an_unknown_member_or_one_of_another_commerce()
    {
        var world = new TestWorld();
        var foreign = world.GivenUser(tenant: TenantId.New());
        world.ActAs(world.Owner, Role.Owner);

        var unknown = await world.ResetUserPin.HandleAsync(new ResetUserPinCommand(UserId.New(), "5555"));
        var other = await world.ResetUserPin.HandleAsync(new ResetUserPinCommand(foreign.Id, "5555"));

        Assert.Equal(ErrorCodes.NotFound, unknown.Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, other.Error!.Code);
    }
}

public class EnableUserTests
{
    private static (TestWorld World, User Target) AsOwnerWithADisabledCashier()
    {
        var world = new TestWorld();
        var target = world.GivenUser(role: Role.Cashier);
        target.Disable(world.Clock.UtcNow.AddDays(-3));
        world.ActAs(world.Owner, Role.Owner);
        return (world, target);
    }

    [Fact]
    public async Task The_owner_reactivates_a_member_with_a_new_pin_and_it_is_audited()
    {
        var (world, target) = AsOwnerWithADisabledCashier();

        var result = await world.EnableUser.HandleAsync(new EnableUserCommand(target.Id, "6666"));

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Active, target.Status);
        Assert.Equal("hash:6666", target.PinHash);
        Assert.Equal(world.Clock.UtcNow, target.SessionsValidFrom);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.UserEnabled, entry.Action);
        Assert.DoesNotContain("6666", entry.Detail);
    }

    [Fact]
    public async Task The_reactivated_member_logs_in_with_the_new_pin_only()
    {
        var (world, target) = AsOwnerWithADisabledCashier();

        await world.EnableUser.HandleAsync(new EnableUserCommand(target.Id, "6666"));
        var old = await world.LogIn.HandleAsync(new LogInCommand(world.Tenant, target.Identifier, "1234"));
        var fresh = await world.LogIn.HandleAsync(new LogInCommand(world.Tenant, target.Identifier, "6666"));

        Assert.False(old.IsSuccess);
        Assert.True(fresh.IsSuccess);
    }

    [Fact]
    public async Task An_active_member_gets_a_clear_message()
    {
        var world = new TestWorld();
        var target = world.GivenUser();
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.EnableUser.HandleAsync(new EnableUserCommand(target.Id, "6666"));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("déjà actif", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal("hash:1234", target.PinHash);
    }

    [Fact]
    public async Task A_new_pin_is_required()
    {
        var (world, target) = AsOwnerWithADisabledCashier();

        var result = await world.EnableUser.HandleAsync(new EnableUserCommand(target.Id, "12"));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Equal(UserStatus.Disabled, target.Status);
    }

    [Fact]
    public async Task A_manager_reactivates_a_cashier_but_not_a_manager()
    {
        var world = new TestWorld();
        var cashier = world.GivenUser("caissier", role: Role.Cashier);
        var manager = world.GivenUser("gerant2", role: Role.Manager);
        cashier.Disable(world.Clock.UtcNow);
        manager.Disable(world.Clock.UtcNow);
        world.ActAs(world.Manager, Role.Manager);

        var allowed = await world.EnableUser.HandleAsync(new EnableUserCommand(cashier.Id, "6666"));
        var refused = await world.EnableUser.HandleAsync(new EnableUserCommand(manager.Id, "6666"));

        Assert.True(allowed.IsSuccess);
        Assert.Equal(ErrorCodes.Forbidden, refused.Error!.Code);
        Assert.Equal(UserStatus.Disabled, manager.Status);
    }

    [Fact]
    public async Task Cashiers_cannot_reactivate_anyone()
    {
        var (world, target) = AsOwnerWithADisabledCashier();
        world.ActAs(UserId.New(), Role.Cashier);

        var result = await world.EnableUser.HandleAsync(new EnableUserCommand(target.Id, "6666"));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task Fails_for_a_member_of_another_commerce()
    {
        var world = new TestWorld();
        var foreign = world.GivenUser(tenant: TenantId.New());
        foreign.Disable(world.Clock.UtcNow);
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.EnableUser.HandleAsync(new EnableUserCommand(foreign.Id, "6666"));

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
    }
}

public class ChangeUserRoleTests
{
    [Theory]
    [InlineData(Role.Cashier, Role.Manager)]
    [InlineData(Role.Cashier, Role.StockKeeper)]
    [InlineData(Role.Cashier, Role.Accountant)]
    [InlineData(Role.Manager, Role.Cashier)]
    [InlineData(Role.StockKeeper, Role.Manager)]
    public async Task The_owner_moves_a_member_to_another_profile_and_it_is_audited(Role from, Role to)
    {
        var world = new TestWorld();
        var target = world.GivenUser(role: from);
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(target.Id, to));

        Assert.True(result.IsSuccess);
        Assert.Equal(to, target.Role);
        Assert.Equal(world.Clock.UtcNow, target.SessionsValidFrom);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.UserRoleChanged, entry.Action);
        Assert.Equal(target.Id.Value, entry.ResourceId);
        Assert.Contains("Awa Koné", entry.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_audit_detail_names_the_member_and_both_profiles_in_french()
    {
        var world = new TestWorld();
        var target = world.GivenUser(role: Role.Cashier);
        world.ActAs(world.Owner, Role.Owner);

        await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(target.Id, Role.Manager));

        Assert.Equal("Awa Koné : Caissier → Gérant", Assert.Single(world.Audit.Entries).Detail);
    }

    [Fact]
    public async Task A_manager_swaps_cashier_and_stock_keeper_only()
    {
        var world = new TestWorld();
        var cashier = world.GivenUser("caissier", role: Role.Cashier);
        var keeper = world.GivenUser("magasinier", role: Role.StockKeeper);
        world.ActAs(world.Manager, Role.Manager);

        var swap = await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(cashier.Id, Role.StockKeeper));
        var promote = await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(keeper.Id, Role.Manager));
        var accountant = await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(keeper.Id, Role.Accountant));

        Assert.True(swap.IsSuccess);
        Assert.Equal(ErrorCodes.Forbidden, promote.Error!.Code);
        Assert.Equal(ErrorCodes.Forbidden, accountant.Error!.Code);
        Assert.Equal(Role.StockKeeper, keeper.Role);
    }

    [Fact]
    public async Task A_manager_cannot_touch_another_manager()
    {
        var world = new TestWorld();
        var other = world.GivenUser("gerant2", role: Role.Manager);
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(other.Id, Role.Cashier));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Equal(Role.Manager, other.Role);
    }

    [Fact]
    public async Task Nobody_becomes_owner()
    {
        var world = new TestWorld();
        var target = world.GivenUser(role: Role.Manager);
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(target.Id, Role.Owner));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Equal(Role.Manager, target.Role);
    }

    [Fact]
    public async Task The_owner_never_changes_role()
    {
        var world = new TestWorld();
        var owner = User.RegisterOwner(
            UserId.New(), world.Tenant, EmailAddress.Parse("mamadou@exemple.ci"), "Mamadou", "password-hash", world.Clock.UtcNow);
        world.Users.Put(owner);
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(owner.Id, Role.Manager));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Equal(Role.Owner, owner.Role);
    }

    [Fact]
    public async Task Nobody_changes_their_own_role()
    {
        var world = new TestWorld();
        var self = world.GivenUser(role: Role.Manager);
        world.ActAs(self.Id, Role.Owner);

        var result = await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(self.Id, Role.Cashier));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Equal(Role.Manager, self.Role);
    }

    [Fact]
    public async Task A_disabled_member_must_be_reactivated_first()
    {
        var world = new TestWorld();
        var target = world.GivenUser(role: Role.Cashier);
        target.Disable(world.Clock.UtcNow);
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(target.Id, Role.Manager));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Equal(Role.Cashier, target.Role);
    }

    [Fact]
    public async Task The_same_role_is_refused_with_a_clear_message()
    {
        var world = new TestWorld();
        var target = world.GivenUser(role: Role.Cashier);
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(target.Id, Role.Cashier));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("déjà ce profil", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(world.Audit.Entries);
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Other_profiles_cannot_change_roles(Role actor)
    {
        var world = new TestWorld();
        var target = world.GivenUser(role: Role.Cashier);
        world.ActAs(UserId.New(), actor);

        var result = await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(target.Id, Role.Manager));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task Fails_for_a_member_of_another_commerce()
    {
        var world = new TestWorld();
        var foreign = world.GivenUser(tenant: TenantId.New());
        world.ActAs(world.Owner, Role.Owner);

        var result = await world.ChangeUserRole.HandleAsync(new ChangeUserRoleCommand(foreign.Id, Role.Manager));

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
    }
}

public class SessionAccessTests
{
    [Fact]
    public async Task A_session_issued_before_a_new_pin_is_outdated_and_a_later_one_is_not()
    {
        var world = new TestWorld();
        var member = world.GivenUser();
        var issued = world.Clock.UtcNow;
        world.Clock.UtcNow = issued.AddMinutes(30);
        member.ResetPin("hash:5555", world.Clock.UtcNow);

        var before = await world.CheckSession.HandleAsync(world.Tenant, member.Id, issued);
        var after = await world.CheckSession.HandleAsync(world.Tenant, member.Id, world.Clock.UtcNow.AddMinutes(1));

        Assert.False(before);
        Assert.True(after);
    }

    [Fact]
    public async Task A_disabled_member_is_refused_even_with_a_recent_session()
    {
        var world = new TestWorld();
        var member = world.GivenUser();
        member.Disable(world.Clock.UtcNow);

        var result = await world.CheckSession.HandleAsync(world.Tenant, member.Id, world.Clock.UtcNow.AddHours(1));

        Assert.False(result);
    }

    [Fact]
    public async Task A_member_who_changed_nothing_keeps_their_session()
    {
        var world = new TestWorld();
        var member = world.GivenUser();

        Assert.True(await world.CheckSession.HandleAsync(world.Tenant, member.Id, world.Clock.UtcNow.AddDays(-1)));
    }

    [Fact]
    public async Task An_unknown_member_is_refused()
    {
        var world = new TestWorld();

        Assert.False(await world.CheckSession.HandleAsync(world.Tenant, UserId.New(), world.Clock.UtcNow));
    }

    [Fact]
    public async Task A_member_of_another_commerce_is_refused()
    {
        var world = new TestWorld();
        var foreign = world.GivenUser(tenant: TenantId.New());

        Assert.False(await world.CheckSession.HandleAsync(world.Tenant, foreign.Id, world.Clock.UtcNow));
    }

    [Fact]
    public async Task The_owner_is_never_cut_off_by_this_check()
    {
        var world = new TestWorld();
        var owner = User.RegisterOwner(
            UserId.New(), world.Tenant, EmailAddress.Parse("mamadou@exemple.ci"), "Mamadou", "password-hash", world.Clock.UtcNow);
        world.Users.Put(owner);

        Assert.True(await world.CheckSession.HandleAsync(world.Tenant, owner.Id, world.Clock.UtcNow.AddDays(-1)));
    }
}

public class TeamViewRightsTests
{
    private static UserView Find(TeamView team, User user) => team.Users.Single(view => view.Id == user.Id.Value);

    [Fact]
    public async Task The_owner_sees_which_actions_each_member_allows()
    {
        var world = new TestWorld();
        var active = world.GivenUser("actif", role: Role.Cashier);
        var disabled = world.GivenUser("parti", role: Role.Cashier);
        disabled.Disable(world.Clock.UtcNow);
        var manager = world.GivenUser("gerant", role: Role.Manager);
        world.ActAs(world.Owner, Role.Owner);

        var team = (await world.ListUsers.HandleAsync()).Value!;

        Assert.True(Find(team, active).CanRenewCode);
        Assert.True(Find(team, active).CanChangeRole);
        Assert.False(Find(team, active).CanEnable);
        Assert.False(Find(team, disabled).CanRenewCode);
        Assert.False(Find(team, disabled).CanChangeRole);
        Assert.True(Find(team, disabled).CanEnable);
        Assert.True(Find(team, manager).CanRenewCode);
    }

    [Fact]
    public async Task A_manager_only_sees_actions_on_cashiers_and_stock_keepers()
    {
        var world = new TestWorld();
        var cashier = world.GivenUser("caissier", role: Role.Cashier);
        var accountant = world.GivenUser("comptable", role: Role.Accountant);
        var other = world.GivenUser("gerant2", role: Role.Manager);
        world.ActAs(world.Manager, Role.Manager);

        var team = (await world.ListUsers.HandleAsync()).Value!;

        Assert.True(Find(team, cashier).CanRenewCode);
        Assert.False(Find(team, accountant).CanRenewCode);
        Assert.False(Find(team, other).CanRenewCode);
        Assert.False(Find(team, other).CanChangeRole);
    }

    [Fact]
    public async Task Nobody_gets_actions_on_themselves()
    {
        var world = new TestWorld();
        var self = world.GivenUser("gerant", role: Role.Manager);
        world.ActAs(self.Id, Role.Owner);

        var team = (await world.ListUsers.HandleAsync()).Value!;

        Assert.False(Find(team, self).CanRenewCode);
        Assert.False(Find(team, self).CanChangeRole);
        Assert.False(Find(team, self).CanEnable);
    }
}

public class DeviceInvitationManagementTests
{
    private static async Task<(TestWorld World, User Creator)> AsManagerWithOneInvitation(string? name = "Caisse 1")
    {
        var world = new TestWorld();
        await world.GivenCommerceAsync();
        var creator = world.GivenUser("gerant", role: Role.Manager);
        world.ActAs(creator.Id, Role.Manager);
        await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand(name));
        world.Audit.Clear();
        return (world, creator);
    }

    [Fact]
    public async Task Lists_pending_invitations_with_the_creator_name_and_no_secret()
    {
        var (world, creator) = await AsManagerWithOneInvitation();

        var result = await world.ListDeviceInvitations.HandleAsync();

        var view = Assert.Single(result.Value!);
        Assert.Equal("Caisse 1", view.Name);
        Assert.Equal(creator.DisplayName, view.CreatedByName);
        Assert.Equal(world.Clock.UtcNow, view.CreatedAt);
        Assert.Equal(world.Clock.UtcNow.AddHours(24), view.ExpiresAt);
        var properties = typeof(PendingInvitationView).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(properties, name => name.Contains("Secret", StringComparison.Ordinal) || name.Contains("Token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unnamed_invitation_is_shown_as_an_unnamed_device()
    {
        var (world, _) = await AsManagerWithOneInvitation(name: null);

        var result = await world.ListDeviceInvitations.HandleAsync();

        Assert.Equal("Appareil sans nom", Assert.Single(result.Value!).Name);
    }

    [Fact]
    public async Task Used_expired_and_revoked_invitations_are_not_listed()
    {
        var (world, _) = await AsManagerWithOneInvitation("utilisé");
        var used = (await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("utilisé 2"))).Value!;
        await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(used.Token, null));
        var revoked = (await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("annulé"))).Value!;
        await world.RevokeDeviceInvitation.HandleAsync(new RevokeDeviceInvitationCommand(revoked.Id));
        world.Clock.UtcNow = world.Clock.UtcNow.AddHours(25);
        var fresh = (await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("récent"))).Value!;

        var result = await world.ListDeviceInvitations.HandleAsync();

        var view = Assert.Single(result.Value!);
        Assert.Equal("récent", view.Name);
        Assert.Equal(fresh.Id.Value, view.Id);
    }

    [Fact]
    public async Task The_newest_invitation_comes_first()
    {
        var (world, _) = await AsManagerWithOneInvitation("première");
        world.Clock.UtcNow = world.Clock.UtcNow.AddHours(1);
        await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("seconde"));

        var result = await world.ListDeviceInvitations.HandleAsync();

        Assert.Equal(["seconde", "première"], result.Value!.Select(view => view.Name));
    }

    [Fact]
    public async Task Revoking_makes_the_link_answer_like_an_expired_one()
    {
        var (world, _) = await AsManagerWithOneInvitation("ancien");
        var expired = (await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("périmé"))).Value!;
        world.Clock.UtcNow = world.Clock.UtcNow.AddHours(25);
        var cancelled = (await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("annulé"))).Value!;

        await world.RevokeDeviceInvitation.HandleAsync(new RevokeDeviceInvitationCommand(cancelled.Id));
        var revokedAnswer = await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(cancelled.Token, null));
        var expiredAnswer = await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(expired.Token, null));

        Assert.Equal(ErrorCodes.InvalidInvitation, revokedAnswer.Error!.Code);
        Assert.Equal(expiredAnswer.Error!.Code, revokedAnswer.Error.Code);
        Assert.Equal(expiredAnswer.Error.Message, revokedAnswer.Error.Message);
        Assert.Equal(0, world.Devices.Count);
    }

    [Fact]
    public async Task Revoking_is_audited_and_repeating_it_is_harmless()
    {
        var (world, _) = await AsManagerWithOneInvitation();
        var id = new DeviceInvitationId(world.DeviceInvitations.All.Single().Id.Value);

        var first = await world.RevokeDeviceInvitation.HandleAsync(new RevokeDeviceInvitationCommand(id));
        var again = await world.RevokeDeviceInvitation.HandleAsync(new RevokeDeviceInvitationCommand(id));

        Assert.True(first.IsSuccess);
        Assert.True(again.IsSuccess);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.DeviceInvitationRevoked, entry.Action);
        Assert.Equal(id.Value, entry.ResourceId);
        Assert.Equal("Caisse 1", entry.Detail);
        Assert.Empty((await world.ListDeviceInvitations.HandleAsync()).Value!);
    }

    [Fact]
    public async Task A_used_invitation_points_to_the_device_list()
    {
        var (world, _) = await AsManagerWithOneInvitation();
        var used = (await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("Caisse 3"))).Value!;
        await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(used.Token, null));

        var result = await world.RevokeDeviceInvitation.HandleAsync(new RevokeDeviceInvitationCommand(used.Id));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("liste des appareils", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fails_for_an_unknown_invitation_or_one_of_another_commerce()
    {
        var (world, _) = await AsManagerWithOneInvitation();
        var foreign = new TestWorld();
        await foreign.GivenCommerceAsync();
        foreign.ActAs(foreign.Manager, Role.Manager);
        var foreignInvitation = (await foreign.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("Caisse X"))).Value!;
        await world.DeviceInvitations.AddAsync(foreign.DeviceInvitations.All.Single(), CancellationToken.None);

        var unknown = await world.RevokeDeviceInvitation.HandleAsync(new RevokeDeviceInvitationCommand(DeviceInvitationId.New()));
        var other = await world.RevokeDeviceInvitation.HandleAsync(new RevokeDeviceInvitationCommand(foreignInvitation.Id));

        Assert.Equal(ErrorCodes.NotFound, unknown.Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, other.Error!.Code);
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Other_profiles_neither_list_nor_revoke(Role role)
    {
        var (world, _) = await AsManagerWithOneInvitation();
        var id = world.DeviceInvitations.All.Single().Id;
        world.ActAs(UserId.New(), role);

        var list = await world.ListDeviceInvitations.HandleAsync();
        var revoke = await world.RevokeDeviceInvitation.HandleAsync(new RevokeDeviceInvitationCommand(id));

        Assert.Equal(ErrorCodes.Forbidden, list.Error!.Code);
        Assert.Equal(ErrorCodes.Forbidden, revoke.Error!.Code);
    }
}
