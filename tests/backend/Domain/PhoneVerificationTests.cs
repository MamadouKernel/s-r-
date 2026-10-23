using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Domain.Tests;

public class PhoneVerificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly PhoneNumber Phone = PhoneNumber.Parse("0707070707");

    private static PhoneVerification Start(VerificationPurpose purpose = VerificationPurpose.Signup, string? payload = "{}") =>
        PhoneVerification.Start(Guid.NewGuid(), purpose, Phone, "hash-1", payload, Now);

    [Fact]
    public void A_new_request_lasts_ten_minutes_and_allows_five_attempts()
    {
        var verification = Start();

        Assert.Equal(Now.AddMinutes(10), verification.ExpiresAt);
        Assert.Equal(PhoneVerification.MaxAttempts, verification.AttemptsLeft);
        Assert.Equal(0, verification.ResendCount);
        Assert.True(verification.IsUsable(Now));
        Assert.Equal(Now.AddSeconds(60), verification.ResendAvailableAt);
    }

    [Fact]
    public void Expires_at_exactly_ten_minutes()
    {
        var verification = Start();

        Assert.True(verification.IsUsable(Now.AddMinutes(10).AddTicks(-1)));
        Assert.False(verification.IsUsable(Now.AddMinutes(10)));
        Assert.True(verification.IsExpired(Now.AddMinutes(10)));
    }

    [Fact]
    public void Five_failed_attempts_block_the_request_for_good()
    {
        var verification = Start();

        for (var attempt = 0; attempt < PhoneVerification.MaxAttempts; attempt++)
        {
            Assert.True(verification.IsUsable(Now));
            verification.RecordFailedAttempt();
        }

        Assert.Equal(0, verification.AttemptsLeft);
        Assert.False(verification.IsUsable(Now));
    }

    [Fact]
    public void A_consumed_request_cannot_be_used_again()
    {
        var verification = Start();

        verification.Consume(Now.AddMinutes(1));

        Assert.True(verification.IsConsumed);
        Assert.False(verification.IsUsable(Now.AddMinutes(2)));
        Assert.Throws<DomainException>(() => verification.Consume(Now.AddMinutes(3)));
    }

    [Fact]
    public void A_request_cannot_be_consumed_once_expired_or_blocked()
    {
        var expired = Start();
        Assert.Throws<DomainException>(() => expired.Consume(Now.AddMinutes(11)));

        var blocked = Start();
        for (var attempt = 0; attempt < PhoneVerification.MaxAttempts; attempt++)
        {
            blocked.RecordFailedAttempt();
        }

        Assert.Throws<DomainException>(() => blocked.Consume(Now));
    }

    [Fact]
    public void A_resend_is_refused_before_sixty_seconds_then_replaces_the_code_and_the_attempts()
    {
        var verification = Start();
        verification.RecordFailedAttempt();

        Assert.False(verification.CanResend(Now.AddSeconds(59)));
        Assert.Throws<DomainException>(() => verification.Reissue("hash-2", Now.AddSeconds(59)));

        Assert.True(verification.CanResend(Now.AddSeconds(60)));
        verification.Reissue("hash-2", Now.AddSeconds(60));

        Assert.Equal("hash-2", verification.CodeHash);
        Assert.Equal(1, verification.ResendCount);
        Assert.Equal(PhoneVerification.MaxAttempts, verification.AttemptsLeft);
        Assert.Equal(Now.AddSeconds(60).AddMinutes(10), verification.ExpiresAt);
        Assert.Equal(Now.AddSeconds(120), verification.ResendAvailableAt);
    }

    [Fact]
    public void At_most_three_resends()
    {
        var verification = Start();
        var at = Now;

        for (var resend = 0; resend < PhoneVerification.MaxResends; resend++)
        {
            at = at.AddSeconds(61);
            verification.Reissue($"hash-{resend}", at);
        }

        at = at.AddSeconds(61);
        Assert.False(verification.CanResend(at));
        Assert.Throws<DomainException>(() => verification.Reissue("hash-x", at));
    }

    [Fact]
    public void An_expired_consumed_or_blocked_request_cannot_be_resent()
    {
        var expired = Start();
        Assert.False(expired.CanResend(Now.AddMinutes(11)));

        var consumed = Start();
        consumed.Consume(Now.AddSeconds(5));
        Assert.False(consumed.CanResend(Now.AddMinutes(1)));
    }

    [Fact]
    public void Keeps_the_purpose_the_phone_and_the_payload()
    {
        var verification = Start(VerificationPurpose.PasswordReset, payload: null);

        Assert.Equal(VerificationPurpose.PasswordReset, verification.Purpose);
        Assert.Equal(Phone, verification.Phone);
        Assert.Null(verification.Payload);
    }

    [Fact]
    public void Refuses_a_blank_code_hash() =>
        Assert.Throws<DomainException>(() => PhoneVerification.Start(Guid.NewGuid(), VerificationPurpose.Signup, Phone, " ", null, Now));
}
