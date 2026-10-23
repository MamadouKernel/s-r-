using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Domain.Tests;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private static User NewUser(string identifier = "awa.kone", Role role = Role.Cashier) =>
        User.Register(UserId.New(), TenantId.New(), identifier, "Awa Koné", role, "hash-1", Now);

    private static void Fail(User user, int times, DateTimeOffset at)
    {
        for (var attempt = 0; attempt < times; attempt++)
        {
            user.RecordFailedAttempt(at);
        }
    }

    [Fact]
    public void Registers_an_active_unlocked_user_with_a_normalized_identifier()
    {
        var user = NewUser("  Awa.Kone ");

        Assert.Equal("awa.kone", user.Identifier);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.CanLogIn(Now));
        Assert.Equal(0, user.FailedAttempts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("x@y.ci")]
    [InlineData(".starts.with.dot")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Rejects_an_invalid_identifier(string identifier) =>
        Assert.Throws<DomainException>(() => NewUser(identifier));

    [Fact]
    public void Rejects_a_blank_display_name() =>
        Assert.Throws<DomainException>(() =>
            User.Register(UserId.New(), TenantId.New(), "awa.kone", " ", Role.Cashier, "hash-1", Now));

    [Fact]
    public void Rejects_a_blank_pin_hash() =>
        Assert.Throws<DomainException>(() =>
            User.Register(UserId.New(), TenantId.New(), "awa.kone", "Awa", Role.Cashier, " ", Now));

    [Fact]
    public void Four_failed_attempts_do_not_lock_the_account()
    {
        var user = NewUser();

        Fail(user, User.MaxFailedAttempts - 1, Now);

        Assert.True(user.CanLogIn(Now));
        Assert.Equal(User.MaxFailedAttempts - 1, user.FailedAttempts);
    }

    [Fact]
    public void The_fifth_failed_attempt_locks_the_account_for_fifteen_minutes()
    {
        var user = NewUser();

        Fail(user, User.MaxFailedAttempts, Now);

        Assert.True(user.IsLocked(Now));
        Assert.False(user.CanLogIn(Now));
        Assert.False(user.CanLogIn(Now.AddMinutes(14)));
        Assert.True(user.CanLogIn(Now.AddMinutes(15)));
    }

    [Fact]
    public void A_failure_while_locked_does_not_extend_the_lock()
    {
        var user = NewUser();
        Fail(user, User.MaxFailedAttempts, Now);

        user.RecordFailedAttempt(Now.AddMinutes(10));

        Assert.True(user.CanLogIn(Now.AddMinutes(15)));
    }

    [Fact]
    public void The_counter_restarts_after_a_lock_so_one_more_failure_does_not_lock_again()
    {
        var user = NewUser();
        Fail(user, User.MaxFailedAttempts, Now);

        user.RecordFailedAttempt(Now.AddMinutes(20));

        Assert.True(user.CanLogIn(Now.AddMinutes(20)));
        Assert.Equal(1, user.FailedAttempts);
    }

    [Fact]
    public void A_successful_login_resets_the_counter_and_records_the_time()
    {
        var user = NewUser();
        Fail(user, 3, Now);

        user.RecordSuccessfulLogin(Now.AddMinutes(1));

        Assert.Equal(0, user.FailedAttempts);
        Assert.Equal(Now.AddMinutes(1), user.LastLoginAt);
    }

    [Fact]
    public void A_locked_account_cannot_record_a_successful_login()
    {
        var user = NewUser();
        Fail(user, User.MaxFailedAttempts, Now);

        Assert.Throws<DomainException>(() => user.RecordSuccessfulLogin(Now.AddMinutes(1)));
    }

    [Fact]
    public void A_disabled_user_cannot_log_in()
    {
        var user = NewUser();

        user.Disable();

        Assert.Equal(UserStatus.Disabled, user.Status);
        Assert.False(user.CanLogIn(Now));
        Assert.Throws<DomainException>(() => user.RecordSuccessfulLogin(Now));
    }

    [Fact]
    public void Disabling_twice_is_harmless()
    {
        var user = NewUser();
        user.Disable();

        user.Disable();

        Assert.Equal(UserStatus.Disabled, user.Status);
    }

    // ---- Propriétaire : téléphone et mot de passe (ADR 0005) ------------------------------------------

    private static readonly PhoneNumber OwnerPhone = PhoneNumber.Parse("0707070707");

    private static User NewOwner() =>
        User.RegisterOwner(UserId.New(), TenantId.New(), OwnerPhone, "  Mamadou Konaté ", "password-hash-1", Now);

    [Fact]
    public void Registers_an_owner_with_a_phone_a_password_and_no_pin()
    {
        var owner = NewOwner();

        Assert.Equal(Role.Owner, owner.Role);
        Assert.Equal("Mamadou Konaté", owner.DisplayName);
        Assert.Equal(OwnerPhone, owner.Phone);
        Assert.Equal("password-hash-1", owner.PasswordHash);
        Assert.Null(owner.PinHash);
        Assert.Equal("proprietaire", owner.Identifier);
        Assert.Equal(UserStatus.Active, owner.Status);
        Assert.True(owner.CanLogIn(Now));
    }

    [Fact]
    public void An_owner_needs_a_name_and_a_password_hash()
    {
        Assert.Throws<DomainException>(() => User.RegisterOwner(UserId.New(), TenantId.New(), OwnerPhone, " ", "hash", Now));
        Assert.Throws<DomainException>(() => User.RegisterOwner(UserId.New(), TenantId.New(), OwnerPhone, "Mamadou", " ", Now));
    }

    [Fact]
    public void A_pin_user_has_no_password_and_no_phone()
    {
        var cashier = NewUser();

        Assert.Null(cashier.PasswordHash);
        Assert.Null(cashier.Phone);
        Assert.Equal("hash-1", cashier.PinHash);
    }

    [Fact]
    public void Changing_the_password_replaces_the_hash_and_lifts_a_lock()
    {
        var owner = NewOwner();
        Fail(owner, User.MaxFailedAttempts, Now);
        Assert.True(owner.IsLocked(Now));

        owner.ChangePassword("password-hash-2");

        Assert.Equal("password-hash-2", owner.PasswordHash);
        Assert.False(owner.IsLocked(Now));
        Assert.Equal(0, owner.FailedAttempts);
        Assert.True(owner.CanLogIn(Now));
    }

    [Fact]
    public void A_password_change_cannot_use_a_blank_hash_and_a_pin_user_cannot_get_one()
    {
        Assert.Throws<DomainException>(() => NewOwner().ChangePassword(" "));
        Assert.Throws<DomainException>(() => NewUser().ChangePassword("password-hash-2"));
    }

    [Fact]
    public void An_owner_locks_after_five_failures_like_any_account()
    {
        var owner = NewOwner();

        Fail(owner, User.MaxFailedAttempts, Now);

        Assert.True(owner.IsLocked(Now));
        Assert.False(owner.CanLogIn(Now));
    }
}
