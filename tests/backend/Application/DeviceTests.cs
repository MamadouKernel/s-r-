using Soro.Application.Common;
using Soro.Application.Devices;
using Soro.Application.Identity;
using Soro.Application.Reporting;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Tenancy;

namespace Soro.Application.Tests;

public class DeviceInvitationTests
{
    private static TestWorld AsManager()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        return world;
    }

    [Fact]
    public async Task A_manager_creates_an_invitation_valid_for_24_hours_and_it_is_audited()
    {
        var world = AsManager();

        var result = await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("Caisse 1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(world.Clock.UtcNow.AddHours(24), result.Value!.ExpiresAt);
        Assert.True(EnrollmentToken.TryParse(result.Value.Token, out var tenant, out var id, out _));
        Assert.Equal(world.Tenant, tenant);
        Assert.Equal(result.Value.Id, id);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.DeviceInvited, entry.Action);
        Assert.Equal(world.Manager, entry.User);
        Assert.DoesNotContain(result.Value.Token, entry.Detail); // SC-A1 : le jeton n'est jamais journalisé
    }

    [Fact]
    public async Task The_secret_is_stored_only_as_a_hash()
    {
        var world = AsManager();

        var result = await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand(null));

        Assert.True(EnrollmentToken.TryParse(result.Value!.Token, out _, out _, out var secret));
        var stored = Assert.Single(world.DeviceInvitations.All);
        Assert.NotEqual(secret, stored.SecretHash);
        Assert.NotEqual(result.Value.Token, stored.SecretHash);
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Other_profiles_cannot_create_invitations(Role role)
    {
        var world = new TestWorld();
        world.ActAs(UserId.New(), role);

        var result = await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand(null));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Empty(world.DeviceInvitations.All);
    }

    [Fact]
    public async Task The_owner_can_create_one_too()
    {
        var world = new TestWorld();
        world.ActAs(world.Owner, Role.Owner);

        Assert.True((await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand(null))).IsSuccess);
    }

    [Fact]
    public async Task A_suggested_name_that_is_too_short_is_refused()
    {
        var world = AsManager();

        var result = await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("C"));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Empty(world.DeviceInvitations.All);
    }

    [Fact]
    public void A_token_that_is_not_made_of_a_commerce_an_invitation_and_a_secret_is_unreadable()
    {
        Assert.False(EnrollmentToken.TryParse(null, out _, out _, out _));
        Assert.False(EnrollmentToken.TryParse("", out _, out _, out _));
        Assert.False(EnrollmentToken.TryParse("abc", out _, out _, out _));
        Assert.False(EnrollmentToken.TryParse(new string('z', 80), out _, out _, out _));
        Assert.False(EnrollmentToken.TryParse(new string('0', 64) + "secret", out _, out _, out _)); // commerce vide
        Assert.False(EnrollmentToken.TryParse(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), out _, out _, out _)); // pas de secret
    }
}

public class EnrollDeviceTests
{
    private static async Task<(TestWorld World, DeviceInvitationCreated Invitation)> Invited(string? suggested = "Caisse 1")
    {
        var world = new TestWorld();
        await world.GivenCommerceAsync();
        world.ActAs(world.Manager, Role.Manager);
        var invitation = (await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand(suggested))).Value!;
        world.Audit.Clear();
        return (world, invitation);
    }

    [Fact]
    public async Task A_device_presents_the_token_and_is_enrolled_with_a_secret_shown_once()
    {
        var (world, invitation) = await Invited();

        var result = await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(invitation.Token, null));

        Assert.True(result.IsSuccess);
        var enrolled = result.Value!;
        Assert.Equal(world.Tenant, enrolled.Tenant);
        Assert.Equal("Supérette du Carrefour", enrolled.CommerceName);
        Assert.Equal("Caisse 1", enrolled.DeviceName); // le nom suggéré par le gérant
        var device = Assert.Single(world.Devices.All);
        Assert.Equal(enrolled.Device, device.Id);
        Assert.NotEqual(enrolled.Secret, device.SecretHash);
        Assert.True(world.DeviceSecrets.Verify(device.Id.Value, enrolled.Secret, device.SecretHash));
        Assert.True(world.DeviceInvitations.All.Single().IsConsumed);
        Assert.Equal(world.Tenant, world.Scope.Entered);
    }

    [Fact]
    public async Task The_enrolment_is_audited_under_the_name_of_who_invited()
    {
        var (world, invitation) = await Invited();

        await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(invitation.Token, null));

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.DeviceEnrolled, entry.Action);
        Assert.Equal(world.Manager, entry.User);
        Assert.DoesNotContain(invitation.Token, entry.Detail);
    }

    [Fact]
    public async Task The_device_may_choose_its_own_name_and_defaults_to_caisse_without_any_suggestion()
    {
        var (world, invitation) = await Invited();
        var chosen = await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(invitation.Token, "  Caisse du fond "));
        Assert.Equal("Caisse du fond", chosen.Value!.DeviceName);

        var (other, secondInvitation) = await Invited(suggested: null);
        var defaulted = await other.EnrollDevice.HandleAsync(new EnrollDeviceCommand(secondInvitation.Token, ""));
        Assert.Equal(EnrollDeviceHandler.DefaultName, defaulted.Value!.DeviceName);
    }

    [Fact]
    public async Task A_token_cannot_be_used_twice()
    {
        var (world, invitation) = await Invited();
        await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(invitation.Token, null));

        var second = await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(invitation.Token, null));

        Assert.Equal(ErrorCodes.InvalidInvitation, second.Error!.Code);
        Assert.Contains("déjà été utilisé", second.Error.Message);
        Assert.Equal(1, world.Devices.Count); // SC-A2
    }

    [Fact]
    public async Task An_invitation_older_than_24_hours_is_expired()
    {
        var (world, invitation) = await Invited();
        world.Clock.UtcNow = world.Clock.UtcNow.AddHours(24);

        var result = await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(invitation.Token, null));

        Assert.Equal(ErrorCodes.InvalidInvitation, result.Error!.Code);
        Assert.Contains("expiré", result.Error.Message);
        Assert.Equal(0, world.Devices.Count); // SC-A3
    }

    [Fact]
    public async Task Forged_tokens_all_get_the_same_answer_with_no_detail()
    {
        var (world, invitation) = await Invited();
        Assert.True(EnrollmentToken.TryParse(invitation.Token, out var tenant, out var id, out var secret));

        var attempts = new[]
        {
            "n'importe quoi",
            invitation.Token[..70], // tronqué : le secret est devenu faux
            EnrollmentToken.Format(tenant, id, secret + "x"),
            EnrollmentToken.Format(tenant, id, "secret999"),
            EnrollmentToken.Format(tenant, DeviceInvitationId.New(), secret), // invitation inconnue
            EnrollmentToken.Format(TenantId.New(), id, secret), // autre commerce : l'invitation y est introuvable
        };

        foreach (var token in attempts)
        {
            var result = await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(token, null));
            Assert.Equal(ErrorCodes.InvalidInvitation, result.Error!.Code);
            Assert.Equal(EnrollDeviceHandler.Invalid.Message, result.Error.Message);
        }

        Assert.Equal(0, world.Devices.Count); // SC-A3
        Assert.False(world.DeviceInvitations.All.Single().IsConsumed);
    }

    [Fact]
    public async Task A_name_that_is_refused_does_not_burn_the_invitation()
    {
        var (world, invitation) = await Invited();

        var refused = await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(invitation.Token, "C"));
        var retried = await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(invitation.Token, "Caisse 2"));

        Assert.Equal(ErrorCodes.RuleViolation, refused.Error!.Code);
        Assert.True(retried.IsSuccess);
    }

    [Fact]
    public async Task An_invitation_of_another_commerce_cannot_be_used_through_a_swapped_prefix()
    {
        var (world, invitation) = await Invited();
        var foreign = TenantId.New();
        await world.GivenCommerceAsync(foreign, "Autre commerce");
        Assert.True(EnrollmentToken.TryParse(invitation.Token, out _, out var id, out var secret));

        var result = await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(EnrollmentToken.Format(foreign, id, secret), null));

        Assert.Equal(ErrorCodes.InvalidInvitation, result.Error!.Code);
        Assert.Equal(0, world.Devices.Count);
    }
}

public class DeviceLoginTests
{
    private static async Task<(TestWorld World, DeviceEnrolled Device, User Cashier)> Enrolled(bool required = true)
    {
        var world = new TestWorld();
        await world.GivenCommerceAsync();
        world.ActAs(world.Manager, Role.Manager);
        var invitation = (await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("Caisse 1"))).Value!;
        var device = (await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(invitation.Token, null))).Value!;
        world.DevicePolicy.Required = required;
        return (world, device, world.GivenUser("awa", "1234"));
    }

    private static LogInCommand Login(TestWorld world, DeviceEnrolled? device, string pin = "1234", string? secret = null) =>
        new(world.Tenant, "awa", pin, device is null ? null : new DeviceCredentials(device.Device, secret ?? device.Secret));

    [Fact]
    public async Task A_cashier_logs_in_from_an_enrolled_device_and_the_token_carries_the_device()
    {
        var (world, device, _) = await Enrolled();

        var result = await world.LogIn.HandleAsync(Login(world, device));

        Assert.True(result.IsSuccess);
        Assert.Contains($"device={device.Device.Value}", result.Value!.Token.Value);
    }

    [Fact]
    public async Task Without_a_device_the_login_is_refused_when_devices_are_required()
    {
        var (world, _, _) = await Enrolled();

        var result = await world.LogIn.HandleAsync(Login(world, null));

        Assert.Equal(ErrorCodes.DeviceNotAuthorized, result.Error!.Code);
    }

    [Fact]
    public async Task Without_a_device_the_login_still_works_when_the_requirement_is_off()
    {
        var (world, _, _) = await Enrolled(required: false);

        Assert.True((await world.LogIn.HandleAsync(Login(world, null))).IsSuccess);
    }

    [Fact]
    public async Task A_wrong_secret_an_unknown_device_and_a_device_of_another_commerce_are_refused()
    {
        var (world, device, _) = await Enrolled();
        var foreign = new TestWorld();
        await foreign.GivenCommerceAsync();

        var wrongSecret = await world.LogIn.HandleAsync(Login(world, device, secret: "secret999"));
        var unknown = await world.LogIn.HandleAsync(new LogInCommand(world.Tenant, "awa", "1234", new DeviceCredentials(DeviceId.New(), device.Secret)));
        var otherCommerce = await world.LogIn.HandleAsync(new LogInCommand(TenantId.New(), "awa", "1234", new DeviceCredentials(device.Device, device.Secret)));

        Assert.All([wrongSecret, unknown, otherCommerce], result => Assert.Equal(ErrorCodes.DeviceNotAuthorized, result.Error!.Code));
        Assert.Equal(2, world.DeviceSecrets.DummyVerifications); // l'inconnu et l'autre commerce coûtent le même calcul
    }

    [Fact]
    public async Task A_refused_device_never_touches_the_failed_attempts_of_the_account()
    {
        var (world, device, cashier) = await Enrolled();

        for (var i = 0; i < 12; i++)
        {
            await world.LogIn.HandleAsync(Login(world, device, pin: "0000", secret: "secret999")); // SC-A5
        }

        var user = (await world.Users.GetAsync(cashier.Id, world.Tenant, CancellationToken.None))!;
        Assert.Equal(0, user.FailedAttempts);
        Assert.True((await world.LogIn.HandleAsync(Login(world, device))).IsSuccess);
    }

    [Fact]
    public async Task A_revoked_device_can_no_longer_log_in_while_another_still_can()
    {
        var (world, device, _) = await Enrolled();
        var secondInvitation = (await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("Caisse 2"))).Value!;
        var second = (await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(secondInvitation.Token, null))).Value!;

        await world.RevokeDevice.HandleAsync(new RevokeDeviceCommand(device.Device));

        Assert.Equal(ErrorCodes.DeviceNotAuthorized, (await world.LogIn.HandleAsync(Login(world, device))).Error!.Code);
        Assert.True((await world.LogIn.HandleAsync(Login(world, second))).IsSuccess); // SC-A6
    }

    [Fact]
    public async Task The_message_for_a_refused_device_is_plain_and_says_what_to_do()
    {
        var (world, device, _) = await Enrolled();
        await world.RevokeDevice.HandleAsync(new RevokeDeviceCommand(device.Device));

        var result = await world.LogIn.HandleAsync(Login(world, device));

        Assert.Contains("Demandez un nouveau lien", result.Error!.Message);
        Assert.DoesNotContain("secret", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public class DeviceManagementTests
{
    private static async Task<(TestWorld World, DeviceEnrolled Device)> OneDevice()
    {
        var world = new TestWorld();
        await world.GivenCommerceAsync();
        world.ActAs(world.Manager, Role.Manager);
        var invitation = (await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("Caisse 1"))).Value!;
        var device = (await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(invitation.Token, null))).Value!;
        world.Audit.Clear();
        return (world, device);
    }

    [Fact]
    public async Task The_list_shows_newest_first_without_any_secret()
    {
        var (world, _) = await OneDevice();
        world.Clock.UtcNow = world.Clock.UtcNow.AddHours(1);
        var invitation = (await world.CreateDeviceInvitation.HandleAsync(new CreateDeviceInvitationCommand("Caisse 2"))).Value!;
        await world.EnrollDevice.HandleAsync(new EnrollDeviceCommand(invitation.Token, null));

        var result = await world.ListDevices.HandleAsync();

        Assert.Equal(["Caisse 2", "Caisse 1"], result.Value!.Select(view => view.Name));
        Assert.All(result.Value!, view => Assert.True(view.IsActive));
        Assert.DoesNotContain(nameof(Device.SecretHash), typeof(DeviceView).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public async Task Only_managers_and_owners_list_and_revoke()
    {
        var (world, device) = await OneDevice();
        world.ActAs(world.Cashier, Role.Cashier);

        Assert.Equal(ErrorCodes.Forbidden, (await world.ListDevices.HandleAsync()).Error!.Code);
        Assert.Equal(ErrorCodes.Forbidden, (await world.RevokeDevice.HandleAsync(new RevokeDeviceCommand(device.Device))).Error!.Code);
        Assert.True(world.Devices.All.Single().IsActive);
    }

    [Fact]
    public async Task Revoking_records_who_and_when_and_is_audited_once()
    {
        var (world, device) = await OneDevice();
        world.Clock.UtcNow = world.Clock.UtcNow.AddDays(2);

        await world.RevokeDevice.HandleAsync(new RevokeDeviceCommand(device.Device));
        await world.RevokeDevice.HandleAsync(new RevokeDeviceCommand(device.Device)); // idempotent

        var stored = world.Devices.All.Single();
        Assert.False(stored.IsActive);
        Assert.Equal(world.Manager, stored.RevokedBy);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.DeviceRevoked, entry.Action);
        Assert.Equal(device.Device.Value, entry.ResourceId);
    }

    [Fact]
    public async Task An_unknown_device_or_one_of_another_commerce_is_not_found()
    {
        var (world, device) = await OneDevice();
        var other = new TestWorld();
        other.ActAs(other.Manager, Role.Manager);

        Assert.Equal(ErrorCodes.NotFound, (await world.RevokeDevice.HandleAsync(new RevokeDeviceCommand(DeviceId.New()))).Error!.Code);
        world.ActAs(world.Manager, Role.Manager);
        var foreign = new FakeCurrentUser(TenantId.New(), UserId.New(), Role.Manager);
        var foreignRevoke = new RevokeDeviceHandler(foreign, world.Devices, world.Audit, world.Clock, world.UnitOfWork);
        Assert.Equal(ErrorCodes.NotFound, (await foreignRevoke.HandleAsync(new RevokeDeviceCommand(device.Device))).Error!.Code); // SC-A7
        Assert.True(world.Devices.All.Single().IsActive);
    }

    [Fact]
    public async Task Every_request_of_a_device_is_checked_and_a_revocation_applies_to_the_next_one()
    {
        var (world, device) = await OneDevice();

        Assert.True(await world.CheckDeviceAccess.IsAllowedAsync(device.Device, world.Tenant));
        await world.RevokeDevice.HandleAsync(new RevokeDeviceCommand(device.Device));

        Assert.False(await world.CheckDeviceAccess.IsAllowedAsync(device.Device, world.Tenant)); // SC-A6
        Assert.False(await world.CheckDeviceAccess.IsAllowedAsync(DeviceId.New(), world.Tenant));
        Assert.False(await world.CheckDeviceAccess.IsAllowedAsync(device.Device, TenantId.New()));
    }

    [Fact]
    public async Task The_last_activity_is_written_at_most_every_five_minutes()
    {
        var (world, device) = await OneDevice();
        var writes = world.Devices.Updates;

        for (var minute = 1; minute <= 4; minute++)
        {
            world.Clock.UtcNow = world.Clock.UtcNow.AddMinutes(1);
            await world.CheckDeviceAccess.IsAllowedAsync(device.Device, world.Tenant);
        }

        Assert.Equal(writes, world.Devices.Updates);

        world.Clock.UtcNow = world.Clock.UtcNow.AddMinutes(1);
        await world.CheckDeviceAccess.IsAllowedAsync(device.Device, world.Tenant);
        Assert.Equal(writes + 1, world.Devices.Updates);
        Assert.Equal(world.Clock.UtcNow, world.Devices.All.Single().LastSeenAt);
    }

    [Fact]
    public void The_audit_journal_has_a_devices_category()
    {
        Assert.True(AuditCategories.TryGetPrefixes("devices", out var prefixes));
        Assert.Contains("device.", prefixes);
    }
}
