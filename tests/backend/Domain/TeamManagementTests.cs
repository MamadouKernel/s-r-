using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Tenancy;

namespace Soro.Domain.Tests;

/// <summary>Gestion complète de l'équipe (docs/specs/equipe-gestion-complete) : nouveau PIN, réactivation, changement de rôle, fin des sessions.</summary>
public class TeamManagementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static User Member(Role role = Role.Cashier) =>
        User.Register(UserId.New(), TenantId.New(), "awa.kone", "Awa Koné", role, "hash-1", Now.AddDays(-10));

    private static User Owner() =>
        User.RegisterOwner(UserId.New(), TenantId.New(), EmailAddress.Parse("mamadou@exemple.ci"), "Mamadou", "password-hash", Now.AddDays(-10));

    [Fact]
    public void A_new_user_has_no_session_cut_off() => Assert.Null(Member().SessionsValidFrom);

    [Fact]
    public void A_new_pin_replaces_the_old_one_lifts_the_lock_and_ends_earlier_sessions()
    {
        var user = Member();
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            user.RecordFailedAttempt(Now);
        }

        user.ResetPin("hash-2", Now.AddMinutes(1));

        Assert.Equal("hash-2", user.PinHash);
        Assert.False(user.IsLocked(Now.AddMinutes(1)));
        Assert.Equal(0, user.FailedAttempts);
        Assert.Equal(Now.AddMinutes(1), user.SessionsValidFrom);
        Assert.True(user.CanLogIn(Now.AddMinutes(1)));
    }

    [Fact]
    public void A_new_pin_needs_a_hash() =>
        Assert.Throws<DomainException>(() => Member().ResetPin(" ", Now));

    [Fact]
    public void A_disabled_member_cannot_get_a_new_pin_without_being_reactivated()
    {
        var user = Member();
        user.Disable(Now);

        Assert.Throws<DomainException>(() => user.ResetPin("hash-2", Now));
    }

    [Fact]
    public void The_owner_has_no_pin_to_reset() =>
        Assert.Throws<DomainException>(() => Owner().ResetPin("hash-2", Now));

    [Fact]
    public void Disabling_ends_earlier_sessions()
    {
        var user = Member();

        user.Disable(Now);

        Assert.Equal(Now, user.SessionsValidFrom);
    }

    [Fact]
    public void Disabling_twice_keeps_the_first_cut_off()
    {
        var user = Member();
        user.Disable(Now);

        user.Disable(Now.AddHours(1));

        Assert.Equal(Now, user.SessionsValidFrom);
    }

    [Fact]
    public void Reactivating_gives_a_new_pin_clears_failures_and_ends_earlier_sessions()
    {
        var user = Member();
        user.Disable(Now);

        user.Reactivate("hash-2", Now.AddDays(2));

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal("hash-2", user.PinHash);
        Assert.Equal(0, user.FailedAttempts);
        Assert.Null(user.LockedUntil);
        Assert.Equal(Now.AddDays(2), user.SessionsValidFrom);
        Assert.True(user.CanLogIn(Now.AddDays(2)));
    }

    [Fact]
    public void Only_a_disabled_member_can_be_reactivated() =>
        Assert.Throws<DomainException>(() => Member().Reactivate("hash-2", Now));

    [Fact]
    public void The_owner_cannot_be_reactivated_with_a_pin()
    {
        var owner = Owner();
        owner.Disable(Now);

        Assert.Throws<DomainException>(() => owner.Reactivate("hash-2", Now));
    }

    [Fact]
    public void Changing_the_role_ends_earlier_sessions()
    {
        var user = Member(Role.Cashier);

        user.ChangeRole(Role.Manager, Now);

        Assert.Equal(Role.Manager, user.Role);
        Assert.Equal(Now, user.SessionsValidFrom);
    }

    [Fact]
    public void Changing_to_the_same_role_is_refused() =>
        Assert.Throws<DomainException>(() => Member(Role.Cashier).ChangeRole(Role.Cashier, Now));

    [Fact]
    public void Nobody_becomes_owner_and_the_owner_never_changes_role()
    {
        Assert.Throws<DomainException>(() => Member().ChangeRole(Role.Owner, Now));
        Assert.Throws<DomainException>(() => Owner().ChangeRole(Role.Manager, Now));
    }

    [Fact]
    public void A_disabled_member_cannot_change_role()
    {
        var user = Member();
        user.Disable(Now);

        Assert.Throws<DomainException>(() => user.ChangeRole(Role.Manager, Now));
    }

    [Fact]
    public void The_cut_off_only_moves_forward()
    {
        var user = Member();
        user.ResetPin("hash-2", Now.AddHours(2));

        user.InvalidateSessions(Now);

        Assert.Equal(Now.AddHours(2), user.SessionsValidFrom);
    }

    [Fact]
    public void Sessions_issued_before_the_cut_off_are_outdated_and_later_ones_are_not()
    {
        var user = Member();
        user.ResetPin("hash-2", Now);

        Assert.True(user.AreSessionsOutdated(Now.AddMinutes(-1)));
        Assert.False(user.AreSessionsOutdated(Now));
        Assert.False(user.AreSessionsOutdated(Now.AddMinutes(1)));
        Assert.False(Member().AreSessionsOutdated(Now.AddYears(-1)));
    }

    [Fact]
    public void The_cut_off_is_compared_to_the_second_because_a_token_only_carries_whole_seconds()
    {
        var user = Member();
        user.ResetPin("hash-2", Now.AddMilliseconds(400));

        Assert.False(user.AreSessionsOutdated(Now));
        Assert.True(user.AreSessionsOutdated(Now.AddSeconds(-1)));
    }
}

public class DeviceInvitationRevocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static DeviceInvitation Issue() =>
        DeviceInvitation.Issue(DeviceInvitationId.New(), TenantId.New(), "hash", UserId.New(), "Caisse 1", Now);

    [Fact]
    public void A_pending_invitation_can_be_revoked_and_is_then_unusable()
    {
        var invitation = Issue();
        Assert.True(invitation.IsPending(Now));

        invitation.Revoke(Now.AddMinutes(5));

        Assert.True(invitation.IsRevoked);
        Assert.Equal(Now.AddMinutes(5), invitation.RevokedAt);
        Assert.False(invitation.IsUsable(Now.AddMinutes(6)));
        Assert.False(invitation.IsPending(Now.AddMinutes(6)));
        Assert.Throws<DomainException>(() => invitation.Consume(Now.AddMinutes(6)));
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_date()
    {
        var invitation = Issue();
        invitation.Revoke(Now.AddMinutes(5));

        invitation.Revoke(Now.AddMinutes(9));

        Assert.Equal(Now.AddMinutes(5), invitation.RevokedAt);
    }

    [Fact]
    public void A_used_invitation_cannot_be_revoked()
    {
        var invitation = Issue();
        invitation.Consume(Now.AddMinutes(1));

        Assert.Throws<DomainException>(() => invitation.Revoke(Now.AddMinutes(2)));
    }

    [Fact]
    public void An_expired_or_used_invitation_is_not_pending()
    {
        var expired = Issue();
        var used = Issue();
        used.Consume(Now.AddMinutes(1));

        Assert.False(expired.IsPending(Now.AddHours(25)));
        Assert.False(used.IsPending(Now.AddMinutes(2)));
    }
}
