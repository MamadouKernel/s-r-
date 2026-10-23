using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Signup;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Tenancy;

namespace Soro.Application.Tests;

public class OwnerLogInTests
{
    private static readonly string Password = string.Join('-', "Soro", "caisse", "2026");
    private static readonly string WrongPassword = string.Join('-', "Mauvais", "mot", "2026");

    /// <summary>Un commerce déjà inscrit, par le parcours réel, pour que la connexion s'appuie sur ce que l'inscription a créé.</summary>
    private static async Task<(TestWorld World, CompleteSignupResult Owner)> Registered()
    {
        var world = new TestWorld();
        var started = (await world.StartSignup.HandleAsync(
            new StartSignupCommand("0707070707", "Supérette du Carrefour", ActivityType.Supermarket, "Mamadou Konaté", "2026-10-brouillon"))).Value!;
        var owner = (await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, world.Sms.LastCode, Password))).Value!;
        return (world, owner);
    }

    [Fact]
    public async Task The_owner_logs_in_with_the_phone_in_any_spelling_and_the_password()
    {
        var (world, owner) = await Registered();

        foreach (var spelling in new[] { "0707070707", "+225 07 07 07 07 07", "07.07.07.07.07" })
        {
            var result = await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand(spelling, Password));

            Assert.True(result.IsSuccess, spelling);
            Assert.Equal(owner.Tenant, result.Value!.Tenant);
            Assert.Equal("Supérette du Carrefour", result.Value.CommerceName);
            Assert.Equal("Mamadou Konaté", result.Value.DisplayName);
            Assert.Contains($":{Role.Owner}", result.Value.Token.Value);
        }

        Assert.Equal(owner.Tenant, world.Scope.Entered);
    }

    [Fact]
    public async Task A_wrong_password_an_unknown_phone_a_malformed_phone_and_a_disabled_account_get_the_same_answer()
    {
        var (world, owner) = await Registered();
        var wrongPassword = await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", WrongPassword));
        var unknown = await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0505050505", Password));
        var malformed = await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("pas un numéro", Password));
        (await world.Users.GetAsync(owner.UserId, owner.Tenant, CancellationToken.None))!.Disable();
        var disabled = await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", Password));

        var answers = new[] { wrongPassword, unknown, malformed, disabled }.Select(result => (result.Error!.Code, result.Error.Message)).Distinct().ToList();
        Assert.Single(answers); // SC-I8
        Assert.Equal(ErrorCodes.InvalidCredentials, answers[0].Code);
    }

    [Fact]
    public async Task An_unknown_phone_costs_as_much_calculation_as_a_known_one()
    {
        var (world, _) = await Registered();

        await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0505050505", Password));
        await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("pas un numéro", Password));

        Assert.Equal(2, world.Passwords.DummyVerifications);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_and_even_the_right_one_is_then_refused_with_a_distinct_answer()
    {
        var (world, _) = await Registered();
        for (var i = 0; i < User.MaxFailedAttempts; i++)
        {
            Assert.Equal(ErrorCodes.InvalidCredentials, (await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", WrongPassword))).Error!.Code);
        }

        var locked = await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", Password));

        Assert.Equal(ErrorCodes.AccountLocked, locked.Error!.Code);
    }

    [Fact]
    public async Task The_lock_ends_after_fifteen_minutes()
    {
        var (world, _) = await Registered();
        for (var i = 0; i < User.MaxFailedAttempts; i++)
        {
            await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", WrongPassword));
        }

        world.Clock.UtcNow = world.Clock.UtcNow.Add(User.LockDuration).AddSeconds(1);
        var result = await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", Password));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_good_login_resets_the_failure_count_and_records_the_time()
    {
        var (world, owner) = await Registered();
        for (var i = 0; i < User.MaxFailedAttempts - 1; i++)
        {
            await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", WrongPassword));
        }

        world.Clock.UtcNow = world.Clock.UtcNow.AddHours(1);
        await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", Password));

        var user = (await world.Users.GetAsync(owner.UserId, owner.Tenant, CancellationToken.None))!;
        Assert.Equal(0, user.FailedAttempts);
        Assert.Equal(world.Clock.UtcNow, user.LastLoginAt);
    }

    [Fact]
    public async Task A_pin_only_member_cannot_log_in_as_an_owner_even_through_the_directory()
    {
        var (world, owner) = await Registered();
        var cashier = world.GivenUser("awa", "1234", Role.Cashier, owner.Tenant);
        await world.OwnerDirectory.AddAsync(new OwnerAccount(PhoneNumber.Parse("0505050505"), owner.Tenant, cashier.Id), CancellationToken.None);

        var result = await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0505050505", "1234"));

        Assert.Equal(ErrorCodes.InvalidCredentials, result.Error!.Code);
    }
}

public class PasswordResetTests
{
    private static readonly string Password = string.Join('-', "Soro", "caisse", "2026");
    private static readonly string WrongPassword = string.Join('-', "Mauvais", "mot", "2026");
    private static readonly string NewPassword = string.Join('-', "Nouveau", "mot", "2027");

    private static async Task<(TestWorld World, CompleteSignupResult Owner)> Registered()
    {
        var world = new TestWorld();
        var started = (await world.StartSignup.HandleAsync(
            new StartSignupCommand("0707070707", "Supérette du Carrefour", ActivityType.Supermarket, "Mamadou Konaté", "2026-10-brouillon"))).Value!;
        var owner = (await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, world.Sms.LastCode, Password))).Value!;
        world.Clock.UtcNow = world.Clock.UtcNow.AddHours(2);
        return (world, owner);
    }

    [Fact]
    public async Task A_known_number_receives_a_code_and_an_unknown_one_gets_the_same_answer_without_any_sms()
    {
        var (world, _) = await Registered();
        var before = world.Sms.Sent.Count;

        var known = await world.StartPasswordReset.HandleAsync(new StartPasswordResetCommand("0707070707"));
        var smsAfterKnown = world.Sms.Sent.Count;
        var unknown = await world.StartPasswordReset.HandleAsync(new StartPasswordResetCommand("0505050505"));

        Assert.True(known.IsSuccess);
        Assert.True(unknown.IsSuccess);
        Assert.Equal(known.Value, unknown.Value); // SC-I9 : même réponse
        Assert.Equal(before + 1, smsAfterKnown);
        Assert.Equal(smsAfterKnown, world.Sms.Sent.Count);
    }

    [Fact]
    public async Task A_malformed_number_is_the_only_thing_reported_and_it_reveals_nothing_about_accounts()
    {
        var (world, _) = await Registered();

        var result = await world.StartPasswordReset.HandleAsync(new StartPasswordResetCommand("12"));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
    }

    [Fact]
    public async Task At_most_three_requests_a_day_and_the_fourth_is_silent()
    {
        var (world, _) = await Registered();
        var before = world.Sms.Sent.Count;

        for (var i = 0; i < 4; i++)
        {
            Assert.True((await world.StartPasswordReset.HandleAsync(new StartPasswordResetCommand("0707070707"))).IsSuccess);
        }

        Assert.Equal(before + StartSignupHandler.MaxStartsPerDay, world.Sms.Sent.Count);
    }

    [Fact]
    public async Task The_right_code_sets_the_new_password_the_old_one_stops_working_and_the_action_is_audited()
    {
        var (world, owner) = await Registered();
        await world.StartPasswordReset.HandleAsync(new StartPasswordResetCommand("0707070707"));

        var result = await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("07 07 07 07 07", world.Sms.LastCode, NewPassword));

        Assert.True(result.IsSuccess);
        Assert.Equal(ErrorCodes.InvalidCredentials, (await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", Password))).Error!.Code);
        Assert.True((await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", NewPassword))).IsSuccess);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.PasswordReset && entry.Tenant == owner.Tenant && entry.User == owner.UserId);
        Assert.DoesNotContain(NewPassword, string.Join(' ', world.Audit.Entries.Select(entry => entry.Detail)));
    }

    [Fact]
    public async Task A_reset_lifts_a_lock()
    {
        var (world, _) = await Registered();
        for (var i = 0; i < User.MaxFailedAttempts; i++)
        {
            await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", WrongPassword));
        }

        Assert.Equal(ErrorCodes.AccountLocked, (await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", Password))).Error!.Code);
        await world.StartPasswordReset.HandleAsync(new StartPasswordResetCommand("0707070707"));
        await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("0707070707", world.Sms.LastCode, NewPassword));

        Assert.True((await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", NewPassword))).IsSuccess);
    }

    [Fact]
    public async Task Every_code_error_gets_the_same_message_whether_or_not_a_request_exists()
    {
        var (world, _) = await Registered();
        await world.StartPasswordReset.HandleAsync(new StartPasswordResetCommand("0707070707"));

        var wrongCode = await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("0707070707", "000000", NewPassword));
        var noRequest = await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("0505050505", "000000", NewPassword));
        var malformed = await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("zzz", "000000", NewPassword));

        var messages = new[] { wrongCode, noRequest, malformed }.Select(result => (result.Error!.Code, result.Error.Message)).Distinct().ToList();
        Assert.Single(messages);
        Assert.Equal(ErrorCodes.InvalidCode, messages[0].Code);
    }

    [Fact]
    public async Task Five_wrong_codes_block_the_request_even_for_the_right_code()
    {
        var (world, _) = await Registered();
        await world.StartPasswordReset.HandleAsync(new StartPasswordResetCommand("0707070707"));
        var right = world.Sms.LastCode;
        for (var i = 0; i < PhoneVerification.MaxAttempts; i++)
        {
            await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("0707070707", "000000", NewPassword));
        }

        var result = await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("0707070707", right, NewPassword));

        Assert.Equal(ErrorCodes.InvalidCode, result.Error!.Code);
        Assert.Equal(ErrorCodes.InvalidCredentials, (await world.OwnerLogIn.HandleAsync(new OwnerLogInCommand("0707070707", NewPassword))).Error!.Code);
    }

    [Fact]
    public async Task An_expired_code_and_a_used_code_are_refused()
    {
        var (world, _) = await Registered();
        await world.StartPasswordReset.HandleAsync(new StartPasswordResetCommand("0707070707"));
        var code = world.Sms.LastCode;
        world.Clock.UtcNow = world.Clock.UtcNow.AddMinutes(11);
        Assert.Equal(ErrorCodes.InvalidCode, (await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("0707070707", code, NewPassword))).Error!.Code);

        await world.StartPasswordReset.HandleAsync(new StartPasswordResetCommand("0707070707"));
        var fresh = world.Sms.LastCode;
        Assert.True((await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("0707070707", fresh, NewPassword))).IsSuccess);
        Assert.Equal(ErrorCodes.InvalidCode, (await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("0707070707", fresh, "Autre-mot-2028"))).Error!.Code);
    }

    [Fact]
    public async Task A_weak_new_password_is_refused_and_does_not_consume_the_code()
    {
        var (world, _) = await Registered();
        await world.StartPasswordReset.HandleAsync(new StartPasswordResetCommand("0707070707"));
        var code = world.Sms.LastCode;

        var weak = await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("0707070707", code, "court1"));
        var strong = await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("0707070707", code, NewPassword));

        Assert.Equal(ErrorCodes.RuleViolation, weak.Error!.Code);
        Assert.True(strong.IsSuccess);
    }

    [Fact]
    public async Task A_signup_code_cannot_reset_a_password()
    {
        var (world, _) = await Registered();
        // La demande d'inscription (déjà consommée) ne sert à rien ici : sans demande de réinitialisation, tout code est refusé.
        var result = await world.ConfirmPasswordReset.HandleAsync(new ConfirmPasswordResetCommand("0707070707", "111111", NewPassword));
        Assert.Equal(ErrorCodes.InvalidCode, result.Error!.Code);
    }
}

public class PasswordPolicyTests
{
    private static readonly PhoneNumber Phone = PhoneNumber.Parse("0707070707");

    [Theory]
    [InlineData("Soro-caisse-2026")]
    [InlineData("abcdefghi1")]
    [InlineData("Été-chaud-2026")]
    [InlineData("1234567890a")]
    public void Accepts_a_long_enough_password_with_a_letter_and_a_digit(string password) =>
        Assert.Null(PasswordPolicy.Check(password, Phone));

    [Theory]
    [InlineData(null, "Choisissez")]
    [InlineData("", "Choisissez")]
    [InlineData("abc123", "au moins 10 caractères")]
    [InlineData("123456789", "au moins 10 caractères")]
    [InlineData("uniquementdeslettres", "une lettre et un chiffre")]
    [InlineData("12345678901234", "une lettre et un chiffre")]
    public void Refuses_a_short_or_single_kind_password_with_a_clear_reason(string? password, string reason) =>
        Assert.Contains(reason, PasswordPolicy.Check(password, Phone));

    [Theory]
    [InlineData("Moi0707070707ici")]
    [InlineData("+2250707070707abc")]
    [InlineData("07-07-07-07-07-a")]
    public void Refuses_a_password_that_contains_the_phone_number(string password) =>
        Assert.Contains("numéro de téléphone", PasswordPolicy.Check(password, Phone));

    [Fact]
    public void Refuses_a_password_over_128_characters() =>
        Assert.Contains("128", PasswordPolicy.Check(new string('a', 127) + "12", Phone));

    [Fact]
    public void Accepts_exactly_128_characters() =>
        Assert.Null(PasswordPolicy.Check(new string('a', 126) + "12", Phone));
}
