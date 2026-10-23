using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Signup;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Tenancy;

namespace Soro.Application.Tests;

public class SignupTests
{
    private static readonly string StrongPassword = string.Join('-', "Soro", "caisse", "2026");

    private static StartSignupCommand Start(
        string phone = "07 07 07 07 07",
        string name = "Supérette du Carrefour",
        string owner = "Mamadou Konaté",
        string terms = "2026-10-brouillon") =>
        new(phone, name, ActivityType.Supermarket, owner, terms);

    private static async Task<(TestWorld World, SignupStarted Started)> Started(TestWorld? existing = null)
    {
        var world = existing ?? new TestWorld();
        var result = await world.StartSignup.HandleAsync(Start());
        return (world, result.Value!);
    }

    // ---- INS-01 Demande ----------------------------------------------------------------------------

    [Fact]
    public async Task A_request_sends_a_code_by_sms_and_creates_nothing_but_the_request_itself()
    {
        var world = new TestWorld();

        var result = await world.StartSignup.HandleAsync(Start());

        Assert.True(result.IsSuccess);
        var (to, message) = Assert.Single(world.Sms.Sent);
        Assert.Equal("+2250707070707", to.Value);
        Assert.Contains(world.Sms.LastCode, message);
        Assert.Equal(6, world.Sms.LastCode.Length);
        Assert.Equal(0, world.Commerces.Count); // SC-I2
        Assert.Equal(0, world.Users.Count);
        Assert.Equal(0, world.OwnerDirectory.Count);
        Assert.Single(world.Verifications.All);
    }

    [Fact]
    public async Task The_code_never_comes_back_in_the_answer_nor_in_the_stored_request()
    {
        var world = new TestWorld();

        var result = await world.StartSignup.HandleAsync(Start());

        var code = world.Sms.LastCode;
        var stored = world.Verifications.All.Single();
        Assert.DoesNotContain(code, System.Text.Json.JsonSerializer.Serialize(result.Value));
        Assert.DoesNotContain(code, stored.Payload ?? string.Empty);
        Assert.NotEqual(code, stored.CodeHash);
        Assert.DoesNotContain("Mamadou", world.Sms.Sent[0].Message); // le SMS ne contient que le code
    }

    [Fact]
    public async Task The_answer_gives_the_expiry_and_when_a_resend_becomes_possible()
    {
        var world = new TestWorld();

        var started = (await world.StartSignup.HandleAsync(Start())).Value!;

        Assert.Equal(world.Clock.UtcNow.AddMinutes(10), started.ExpiresAt);
        Assert.Equal(world.Clock.UtcNow.AddSeconds(60), started.ResendAvailableAt);
        Assert.NotEqual(Guid.Empty, started.VerificationId);
    }

    [Theory]
    [InlineData("0707")]
    [InlineData("pas un numéro")]
    [InlineData("+33 6 12 34 56 78")]
    public async Task An_invalid_phone_is_refused_and_no_sms_is_sent(string phone)
    {
        var world = new TestWorld();

        var result = await world.StartSignup.HandleAsync(Start(phone: phone));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Empty(world.Sms.Sent);
        Assert.Empty(world.Verifications.All);
    }

    [Theory]
    [InlineData("", "Mamadou")]
    [InlineData("A", "Mamadou")]
    [InlineData("Supérette", "")]
    [InlineData("Supérette", "   ")]
    public async Task A_missing_commerce_or_owner_name_is_refused_before_any_sms(string name, string owner)
    {
        var world = new TestWorld();

        var result = await world.StartSignup.HandleAsync(Start(name: name, owner: owner));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Empty(world.Sms.Sent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1999-01-ancien")]
    public async Task Terms_that_are_missing_or_not_the_current_version_are_refused(string terms)
    {
        var world = new TestWorld();

        var result = await world.StartSignup.HandleAsync(Start(terms: terms));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("conditions d'utilisation", result.Error.Message);
        Assert.Empty(world.Sms.Sent);
    }

    [Fact]
    public async Task A_phone_that_already_has_an_account_is_refused_without_any_sms_and_with_a_helpful_message()
    {
        var world = new TestWorld();
        var (_, started) = await Started(world);
        await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, world.Sms.LastCode, StrongPassword));
        var before = world.Sms.Sent.Count;

        var again = await world.StartSignup.HandleAsync(Start(phone: "+225 07 07 07 07 07"));

        Assert.Equal(ErrorCodes.PhoneAlreadyRegistered, again.Error!.Code);
        Assert.Contains("connectez-vous", again.Error.Message);
        Assert.Equal(before, world.Sms.Sent.Count);
    }

    [Fact]
    public async Task A_phone_may_ask_three_times_a_day_not_four_and_each_spelling_counts_together()
    {
        var world = new TestWorld();
        foreach (var spelling in new[] { "0707070707", "+225 07 07 07 07 07", "07.07.07.07.07" })
        {
            Assert.True((await world.StartSignup.HandleAsync(Start(phone: spelling))).IsSuccess);
        }

        var fourth = await world.StartSignup.HandleAsync(Start(phone: "002250707070707"));

        Assert.Equal(ErrorCodes.TooManyRequests, fourth.Error!.Code); // SC-I6
        Assert.Equal(3, world.Sms.Sent.Count);
    }

    [Fact]
    public async Task The_daily_limit_is_per_phone_and_resets_after_a_day()
    {
        var world = new TestWorld();
        for (var i = 0; i < 3; i++)
        {
            await world.StartSignup.HandleAsync(Start());
        }

        Assert.True((await world.StartSignup.HandleAsync(Start(phone: "0505050505"))).IsSuccess);

        world.Clock.UtcNow = world.Clock.UtcNow.AddDays(1).AddMinutes(1);
        Assert.True((await world.StartSignup.HandleAsync(Start())).IsSuccess);
    }

    // ---- INS-02 Renvoi -----------------------------------------------------------------------------

    [Fact]
    public async Task A_resend_is_refused_before_sixty_seconds_with_the_remaining_wait()
    {
        var (world, started) = await Started();
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(20);

        var result = await world.ResendSignupCode.HandleAsync(new ResendSignupCodeCommand(started.VerificationId));

        Assert.Equal(ErrorCodes.TooManyRequests, result.Error!.Code);
        Assert.Contains("40 secondes", result.Error.Message);
        Assert.Single(world.Sms.Sent);
    }

    [Fact]
    public async Task A_resend_replaces_the_code_so_the_old_one_no_longer_works_and_the_new_one_does()
    {
        var (world, started) = await Started();
        var oldCode = world.Sms.LastCode;
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(61);

        var resent = await world.ResendSignupCode.HandleAsync(new ResendSignupCodeCommand(started.VerificationId));

        Assert.True(resent.IsSuccess);
        var newCode = world.Sms.LastCode;
        Assert.NotEqual(oldCode, newCode);
        var withOld = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, oldCode, StrongPassword));
        Assert.Equal(ErrorCodes.InvalidCode, withOld.Error!.Code);
        var withNew = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, newCode, StrongPassword));
        Assert.True(withNew.IsSuccess);
    }

    [Fact]
    public async Task At_most_three_resends_then_the_user_must_start_again()
    {
        var (world, started) = await Started();
        for (var i = 0; i < PhoneVerification.MaxResends; i++)
        {
            world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(61);
            Assert.True((await world.ResendSignupCode.HandleAsync(new ResendSignupCodeCommand(started.VerificationId))).IsSuccess);
        }

        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(61);
        var fourth = await world.ResendSignupCode.HandleAsync(new ResendSignupCodeCommand(started.VerificationId));

        Assert.Equal(ErrorCodes.TooManyRequests, fourth.Error!.Code);
        Assert.Contains("recommencez", fourth.Error.Message);
        Assert.Equal(1 + PhoneVerification.MaxResends, world.Sms.Sent.Count);
    }

    [Fact]
    public async Task Resending_an_unknown_expired_or_foreign_request_is_refused_the_same_way()
    {
        var (world, started) = await Started();
        var unknown = await world.ResendSignupCode.HandleAsync(new ResendSignupCodeCommand(Guid.NewGuid()));
        world.Clock.UtcNow = world.Clock.UtcNow.AddMinutes(11);
        var expired = await world.ResendSignupCode.HandleAsync(new ResendSignupCodeCommand(started.VerificationId));

        Assert.Equal(ErrorCodes.VerificationExpired, unknown.Error!.Code);
        Assert.Equal(ErrorCodes.VerificationExpired, expired.Error!.Code);
    }

    // ---- INS-03 Finalisation -----------------------------------------------------------------------

    [Fact]
    public async Task The_right_code_creates_the_commerce_the_owner_and_the_directory_entry_and_signs_the_owner_in()
    {
        var (world, started) = await Started();

        var result = await world.CompleteSignup.HandleAsync(
            new CompleteSignupCommand(started.VerificationId, world.Sms.LastCode, StrongPassword));

        Assert.True(result.IsSuccess);
        var done = result.Value!;
        Assert.Equal(1, world.Commerces.Count);
        var commerce = (await world.Commerces.GetAsync(done.Tenant, CancellationToken.None))!;
        Assert.Equal("Supérette du Carrefour", commerce.Name);
        Assert.Equal(world.Clock.UtcNow.AddDays(30), commerce.TrialEndsAt);
        Assert.Equal("2026-10-brouillon", commerce.TermsVersion);
        Assert.Equal(world.Clock.UtcNow, commerce.TermsAcceptedAt);
        var owner = (await world.Users.GetAsync(done.UserId, done.Tenant, CancellationToken.None))!;
        Assert.Equal(Role.Owner, owner.Role);
        Assert.Equal("pwd-hash:" + StrongPassword, owner.PasswordHash);
        Assert.Equal(done.Tenant, world.Scope.Entered);
        var account = await world.OwnerDirectory.FindAsync(PhoneNumber.Parse("0707070707"), CancellationToken.None);
        Assert.Equal(done.Tenant, account!.Tenant);
        Assert.Equal(done.UserId, account.User);
        Assert.Contains($":{Role.Owner}", done.Token.Value);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.CommerceCreated && entry.Tenant == done.Tenant);
        Assert.True(world.Verifications.All.Single().IsConsumed);
    }

    [Fact]
    public async Task The_new_commerce_is_a_new_tenant_each_time()
    {
        var first = await Started();
        var a = (await first.World.CompleteSignup.HandleAsync(
            new CompleteSignupCommand(first.Started.VerificationId, first.World.Sms.LastCode, StrongPassword))).Value!;
        var world = first.World;
        var second = (await world.StartSignup.HandleAsync(Start(phone: "0505050505", name: "Boutique Awa"))).Value!;
        var b = (await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(second.VerificationId, world.Sms.LastCode, StrongPassword))).Value!;

        Assert.NotEqual(a.Tenant, b.Tenant);
        Assert.Equal(2, world.Commerces.Count);
    }

    [Fact]
    public async Task A_wrong_code_costs_an_attempt_and_says_how_many_remain()
    {
        var (world, started) = await Started();

        var first = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, "000000", StrongPassword));

        Assert.Equal(ErrorCodes.InvalidCode, first.Error!.Code);
        Assert.Contains("4 essais", first.Error.Message);
        Assert.Equal(0, world.Commerces.Count);
        Assert.Equal(PhoneVerification.MaxAttempts - 1, world.Verifications.All.Single().AttemptsLeft);
    }

    [Fact]
    public async Task After_five_wrong_codes_even_the_right_one_is_refused_for_good()
    {
        var (world, started) = await Started();
        var right = world.Sms.LastCode;
        for (var i = 0; i < PhoneVerification.MaxAttempts; i++)
        {
            await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, "000000", StrongPassword));
        }

        var result = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, right, StrongPassword));

        Assert.Equal(ErrorCodes.VerificationExpired, result.Error!.Code); // SC-I3
        Assert.Equal(0, world.Commerces.Count);
    }

    [Fact]
    public async Task The_fifth_wrong_code_says_to_start_again_instead_of_zero_attempts()
    {
        var (world, started) = await Started();
        Result<CompleteSignupResult>? last = null;
        for (var i = 0; i < PhoneVerification.MaxAttempts; i++)
        {
            last = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, "000000", StrongPassword));
        }

        Assert.Equal(ErrorCodes.VerificationExpired, last!.Error!.Code);
        Assert.Contains("recommencez", last.Error.Message);
    }

    [Fact]
    public async Task An_expired_code_is_refused_even_if_correct()
    {
        var (world, started) = await Started();
        world.Clock.UtcNow = world.Clock.UtcNow.AddMinutes(10);

        var result = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, world.Sms.LastCode, StrongPassword));

        Assert.Equal(ErrorCodes.VerificationExpired, result.Error!.Code);
        Assert.Equal(0, world.Commerces.Count);
    }

    [Theory]
    [InlineData("court1")]
    [InlineData("uniquementdeslettres")]
    [InlineData("12345678901234")]
    [InlineData("MonNumero0707070707")]
    public async Task A_weak_password_is_refused_and_does_not_consume_the_code(string password)
    {
        var (world, started) = await Started();
        var code = world.Sms.LastCode;

        var weak = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, code, password));

        Assert.Equal(ErrorCodes.RuleViolation, weak.Error!.Code);
        Assert.False(world.Verifications.All.Single().IsConsumed);
        Assert.Equal(0, world.Commerces.Count);

        var strong = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, code, StrongPassword));
        Assert.True(strong.IsSuccess); // SC-I4
    }

    [Fact]
    public async Task A_used_code_cannot_create_a_second_commerce()
    {
        var (world, started) = await Started();
        var code = world.Sms.LastCode;
        await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, code, StrongPassword));

        var again = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, code, StrongPassword));

        Assert.Equal(ErrorCodes.VerificationExpired, again.Error!.Code);
        Assert.Equal(1, world.Commerces.Count);
    }

    [Fact]
    public async Task An_unknown_request_and_a_password_reset_request_cannot_complete_a_signup()
    {
        var world = new TestWorld();
        var unknown = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(Guid.NewGuid(), "111111", StrongPassword));
        Assert.Equal(ErrorCodes.VerificationExpired, unknown.Error!.Code);

        var reset = PhoneVerification.Start(
            Guid.NewGuid(), VerificationPurpose.PasswordReset, PhoneNumber.Parse("0707070707"), world.Codes.Hash(Guid.Empty, "111111"), null, world.Clock.UtcNow);
        await world.Verifications.AddAsync(reset, CancellationToken.None);
        var wrongKind = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(reset.Id, "111111", StrongPassword));
        Assert.Equal(ErrorCodes.VerificationExpired, wrongKind.Error!.Code);
    }

    [Fact]
    public async Task A_number_registered_in_the_meantime_is_refused_at_the_end_too()
    {
        var (world, first) = await Started();
        var firstCode = world.Sms.LastCode;
        var second = (await world.StartSignup.HandleAsync(Start())).Value!;
        await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(second.VerificationId, world.Sms.LastCode, StrongPassword));

        // Le premier code est encore valable, mais le numéro a désormais un compte.
        var late = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(first.VerificationId, firstCode, StrongPassword));

        Assert.Equal(ErrorCodes.PhoneAlreadyRegistered, late.Error!.Code);
        Assert.Equal(1, world.Commerces.Count);
    }
}
