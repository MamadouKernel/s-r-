using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Domain.Tests;

public class DeviceInvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly UserId Creator = UserId.New();

    private static DeviceInvitation Issue(string? name = "Caisse 1") =>
        DeviceInvitation.Issue(DeviceInvitationId.New(), Tenant, "hash", Creator, name, Now);

    [Fact]
    public void Is_valid_for_24_hours_and_not_a_second_longer()
    {
        var invitation = Issue();

        Assert.Equal(Now.AddHours(24), invitation.ExpiresAt);
        Assert.True(invitation.IsUsable(Now.AddHours(24).AddTicks(-1)));
        Assert.False(invitation.IsUsable(Now.AddHours(24)));
        Assert.True(invitation.IsExpired(Now.AddHours(24)));
    }

    [Fact]
    public void Can_be_consumed_once()
    {
        var invitation = Issue();

        invitation.Consume(Now.AddMinutes(5));

        Assert.True(invitation.IsConsumed);
        Assert.Equal(Now.AddMinutes(5), invitation.ConsumedAt);
        Assert.False(invitation.IsUsable(Now.AddMinutes(6)));
        Assert.Throws<DomainException>(() => invitation.Consume(Now.AddMinutes(6)));
    }

    [Fact]
    public void Cannot_be_consumed_after_expiry() =>
        Assert.Throws<DomainException>(() => Issue().Consume(Now.AddHours(25)));

    [Theory]
    [InlineData("C")]
    [InlineData("  ")]
    public void Refuses_a_suggested_name_that_is_too_short(string name) =>
        Assert.Throws<DomainException>(() => Issue(name));

    [Fact]
    public void Refuses_a_suggested_name_over_50_characters() =>
        Assert.Throws<DomainException>(() => Issue(new string('a', 51)));

    [Fact]
    public void The_suggested_name_is_optional_and_trimmed()
    {
        Assert.Null(Issue(null).SuggestedName);
        Assert.Equal("Caisse 1", Issue("  Caisse 1 ").SuggestedName);
    }

    [Fact]
    public void Requires_a_secret_hash() =>
        Assert.Throws<DomainException>(() => DeviceInvitation.Issue(DeviceInvitationId.New(), Tenant, " ", Creator, null, Now));
}

public class DeviceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly UserId Manager = UserId.New();

    private static Device Enroll(string name = "Caisse 1") =>
        Device.Enroll(DeviceId.New(), Tenant, name, "hash", DeviceInvitationId.New(), Now);

    [Fact]
    public void A_new_device_is_active_and_has_never_been_seen_after_enrolment()
    {
        var device = Enroll("  Caisse du fond ");

        Assert.Equal("Caisse du fond", device.Name);
        Assert.True(device.IsActive);
        Assert.Equal(Now, device.EnrolledAt);
        Assert.Equal(Now, device.LastSeenAt);
        Assert.Null(device.RevokedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C")]
    public void Refuses_a_short_name(string name) => Assert.Throws<DomainException>(() => Enroll(name));

    [Fact]
    public void Refuses_a_name_over_50_characters() => Assert.Throws<DomainException>(() => Enroll(new string('a', 51)));

    [Fact]
    public void Revoking_makes_it_inactive_and_remembers_who_and_when()
    {
        var device = Enroll();

        device.Revoke(Manager, Now.AddDays(3));

        Assert.False(device.IsActive);
        Assert.Equal(Now.AddDays(3), device.RevokedAt);
        Assert.Equal(Manager, device.RevokedBy);
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_revocation()
    {
        var device = Enroll();
        device.Revoke(Manager, Now.AddDays(1));

        device.Revoke(UserId.New(), Now.AddDays(5));

        Assert.Equal(Now.AddDays(1), device.RevokedAt);
        Assert.Equal(Manager, device.RevokedBy);
    }

    [Fact]
    public void Last_seen_only_moves_forward_and_only_when_it_is_worth_a_write()
    {
        var device = Enroll();

        Assert.False(device.Touch(Now.AddMinutes(4)));
        Assert.Equal(Now, device.LastSeenAt);

        Assert.True(device.Touch(Now.AddMinutes(5)));
        Assert.Equal(Now.AddMinutes(5), device.LastSeenAt);

        Assert.False(device.Touch(Now.AddMinutes(1)));
        Assert.Equal(Now.AddMinutes(5), device.LastSeenAt);
    }

    [Fact]
    public void A_revoked_device_is_not_touched()
    {
        var device = Enroll();
        device.Revoke(Manager, Now.AddMinutes(1));

        Assert.False(device.Touch(Now.AddHours(1)));
    }

    [Fact]
    public void Requires_a_secret_hash() =>
        Assert.Throws<DomainException>(() => Device.Enroll(DeviceId.New(), Tenant, "Caisse", " ", DeviceInvitationId.New(), Now));
}
