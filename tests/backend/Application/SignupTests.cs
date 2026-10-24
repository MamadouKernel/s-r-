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
        string email = "mamadou@exemple.ci",
        string name = "Supérette du Carrefour",
        string owner = "Mamadou Konaté",
        string terms = "2026-10-brouillon") =>
        new(email, name, ActivityType.Supermarket, owner, terms);

    private static async Task<(TestWorld World, SignupStarted Started)> Started(TestWorld? existing = null)
    {
        var world = existing ?? new TestWorld();
        var result = await world.StartSignup.HandleAsync(Start());
        return (world, result.Value!);
    }

    // ---- INS-01 Demande ----------------------------------------------------------------------------

    [Fact]
    public async Task A_request_sends_a_code_by_email_and_creates_nothing_but_the_request_itself()
    {
        var world = new TestWorld();

        var result = await world.StartSignup.HandleAsync(Start());

        Assert.True(result.IsSuccess);
        var (to, message) = Assert.Single(world.Mail.Sent);
        Assert.Equal("mamadou@exemple.ci", to.Value);
        Assert.Contains(world.Mail.LastCode, message);
        Assert.Equal(6, world.Mail.LastCode.Length);
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

        var code = world.Mail.LastCode;
        var stored = world.Verifications.All.Single();
        Assert.DoesNotContain(code, System.Text.Json.JsonSerializer.Serialize(result.Value));
        Assert.DoesNotContain(code, stored.Payload ?? string.Empty);
        Assert.NotEqual(code, stored.CodeHash);
        Assert.DoesNotContain("Mamadou", world.Mail.Sent[0].Message); // l'e-mail ne contient que le code
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
    [InlineData("mamadou@")]
    [InlineData("pas une adresse")]
    [InlineData("+33 6 12 34 56 78")]
    public async Task An_invalid_email_is_refused_and_no_email_is_sent(string email)
    {
        var world = new TestWorld();

        var result = await world.StartSignup.HandleAsync(Start(email: email));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Empty(world.Mail.Sent);
        Assert.Empty(world.Verifications.All);
    }

    [Theory]
    [InlineData("", "Mamadou")]
    [InlineData("A", "Mamadou")]
    [InlineData("Supérette", "")]
    [InlineData("Supérette", "   ")]
    public async Task A_missing_commerce_or_owner_name_is_refused_before_any_email(string name, string owner)
    {
        var world = new TestWorld();

        var result = await world.StartSignup.HandleAsync(Start(name: name, owner: owner));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Empty(world.Mail.Sent);
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
        Assert.Empty(world.Mail.Sent);
    }

    [Fact]
    public async Task An_email_that_already_has_an_account_is_refused_without_any_email_and_with_a_helpful_message()
    {
        var world = new TestWorld();
        var (_, started) = await Started(world);
        await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, world.Mail.LastCode, StrongPassword));
        var before = world.Mail.Sent.Count;

        var again = await world.StartSignup.HandleAsync(Start(email: "  Mamadou@Exemple.CI "));

        Assert.Equal(ErrorCodes.EmailAlreadyRegistered, again.Error!.Code);
        Assert.Contains("connectez-vous", again.Error.Message);
        Assert.Equal(before, world.Mail.Sent.Count);
    }

    [Fact]
    public async Task An_email_may_ask_three_times_a_day_not_four_and_each_spelling_counts_together()
    {
        var world = new TestWorld();
        foreach (var spelling in new[] { "mamadou@exemple.ci", "  Mamadou@Exemple.CI ", "MAMADOU@exemple.ci" })
        {
            Assert.True((await world.StartSignup.HandleAsync(Start(email: spelling))).IsSuccess);
        }

        var fourth = await world.StartSignup.HandleAsync(Start(email: " mamadou@EXEMPLE.ci "));

        Assert.Equal(ErrorCodes.TooManyRequests, fourth.Error!.Code); // SC-I6
        Assert.Equal(3, world.Mail.Sent.Count);
    }

    [Fact]
    public async Task The_daily_limit_is_per_email_and_resets_after_a_day()
    {
        var world = new TestWorld();
        for (var i = 0; i < 3; i++)
        {
            await world.StartSignup.HandleAsync(Start());
        }

        Assert.True((await world.StartSignup.HandleAsync(Start(email: "awa@exemple.ci"))).IsSuccess);

        world.Clock.UtcNow = world.Clock.UtcNow.AddDays(1).AddMinutes(1);
        Assert.True((await world.StartSignup.HandleAsync(Start())).IsSuccess);
    }

    [Fact]
    public async Task When_the_mail_server_is_down_the_user_is_told_in_plain_words_and_may_try_again()
    {
        var world = new TestWorld();
        world.Mail.Failing = true;

        var result = await world.StartSignup.HandleAsync(Start());

        Assert.Equal(ErrorCodes.EmailNotSent, result.Error!.Code);
        Assert.Contains("pas pu envoyer", result.Error.Message);
        Assert.DoesNotContain("SMTP", result.Error.Message, StringComparison.OrdinalIgnoreCase);

        world.Mail.Failing = false;
        Assert.True((await world.StartSignup.HandleAsync(Start())).IsSuccess);
    }

    [Fact]
    public async Task A_resend_that_cannot_be_delivered_says_so_and_keeps_the_request()
    {
        var (world, started) = await Started();
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(61);
        world.Mail.Failing = true;

        var result = await world.ResendSignupCode.HandleAsync(new ResendSignupCodeCommand(started.VerificationId));

        Assert.Equal(ErrorCodes.EmailNotSent, result.Error!.Code);
        world.Mail.Failing = false;
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(61);
        Assert.True((await world.ResendSignupCode.HandleAsync(new ResendSignupCodeCommand(started.VerificationId))).IsSuccess);
    }

    [Fact]
    public async Task The_email_carries_the_code_and_plain_instructions_but_nothing_about_the_trader()
    {
        var world = new TestWorld();

        await world.StartSignup.HandleAsync(Start());

        var message = world.Mail.Sent[0].Message;
        Assert.Contains(world.Mail.LastCode, message);
        Assert.Contains("10 minutes", message);
        Assert.Contains("Ne le communiquez à personne", message);
        Assert.DoesNotContain("Supérette", message);
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
        Assert.Single(world.Mail.Sent);
    }

    [Fact]
    public async Task A_resend_replaces_the_code_so_the_old_one_no_longer_works_and_the_new_one_does()
    {
        var (world, started) = await Started();
        var oldCode = world.Mail.LastCode;
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(61);

        var resent = await world.ResendSignupCode.HandleAsync(new ResendSignupCodeCommand(started.VerificationId));

        Assert.True(resent.IsSuccess);
        var newCode = world.Mail.LastCode;
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
        for (var i = 0; i < EmailVerification.MaxResends; i++)
        {
            world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(61);
            Assert.True((await world.ResendSignupCode.HandleAsync(new ResendSignupCodeCommand(started.VerificationId))).IsSuccess);
        }

        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(61);
        var fourth = await world.ResendSignupCode.HandleAsync(new ResendSignupCodeCommand(started.VerificationId));

        Assert.Equal(ErrorCodes.TooManyRequests, fourth.Error!.Code);
        Assert.Contains("recommencez", fourth.Error.Message);
        Assert.Equal(1 + EmailVerification.MaxResends, world.Mail.Sent.Count);
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
            new CompleteSignupCommand(started.VerificationId, world.Mail.LastCode, StrongPassword));

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
        var account = await world.OwnerDirectory.FindAsync(EmailAddress.Parse("mamadou@exemple.ci"), CancellationToken.None);
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
            new CompleteSignupCommand(first.Started.VerificationId, first.World.Mail.LastCode, StrongPassword))).Value!;
        var world = first.World;
        var second = (await world.StartSignup.HandleAsync(Start(email: "awa@exemple.ci", name: "Boutique Awa"))).Value!;
        var b = (await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(second.VerificationId, world.Mail.LastCode, StrongPassword))).Value!;

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
        Assert.Equal(EmailVerification.MaxAttempts - 1, world.Verifications.All.Single().AttemptsLeft);
    }

    [Fact]
    public async Task After_five_wrong_codes_even_the_right_one_is_refused_for_good()
    {
        var (world, started) = await Started();
        var right = world.Mail.LastCode;
        for (var i = 0; i < EmailVerification.MaxAttempts; i++)
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
        for (var i = 0; i < EmailVerification.MaxAttempts; i++)
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

        var result = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(started.VerificationId, world.Mail.LastCode, StrongPassword));

        Assert.Equal(ErrorCodes.VerificationExpired, result.Error!.Code);
        Assert.Equal(0, world.Commerces.Count);
    }

    [Theory]
    [InlineData("court1")]
    [InlineData("uniquementdeslettres")]
    [InlineData("12345678901234")]
    [InlineData("MonMamadou2026")]
    public async Task A_weak_password_is_refused_and_does_not_consume_the_code(string password)
    {
        var (world, started) = await Started();
        var code = world.Mail.LastCode;

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
        var code = world.Mail.LastCode;
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

        var reset = EmailVerification.Start(
            Guid.NewGuid(), VerificationPurpose.PasswordReset, EmailAddress.Parse("mamadou@exemple.ci"), world.Codes.Hash(Guid.Empty, "111111"), null, world.Clock.UtcNow);
        await world.Verifications.AddAsync(reset, CancellationToken.None);
        var wrongKind = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(reset.Id, "111111", StrongPassword));
        Assert.Equal(ErrorCodes.VerificationExpired, wrongKind.Error!.Code);
    }

    [Fact]
    public async Task A_number_registered_in_the_meantime_is_refused_at_the_end_too()
    {
        var (world, first) = await Started();
        var firstCode = world.Mail.LastCode;
        var second = (await world.StartSignup.HandleAsync(Start())).Value!;
        await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(second.VerificationId, world.Mail.LastCode, StrongPassword));

        // Le premier code est encore valable, mais l'adresse a désormais un compte.
        var late = await world.CompleteSignup.HandleAsync(new CompleteSignupCommand(first.VerificationId, firstCode, StrongPassword));

        Assert.Equal(ErrorCodes.EmailAlreadyRegistered, late.Error!.Code);
        Assert.Equal(1, world.Commerces.Count);
    }
}
