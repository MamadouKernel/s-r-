using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

public class LogInTests
{
    private static LogInCommand Login(TestWorld world, string identifier = "awa.kone", string pin = "1234") =>
        new(world.Tenant, identifier, pin);

    [Fact]
    public async Task Logs_in_with_the_right_pin_and_issues_a_token()
    {
        var world = new TestWorld();
        var user = world.GivenUser(role: Role.Manager);

        var result = await world.LogIn.HandleAsync(Login(world));

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value!.UserId);
        Assert.Equal(Role.Manager, result.Value.Role);
        Assert.Equal($"token:{world.Tenant.Value}:{user.Id.Value}:Manager", result.Value.Token.Value);
        Assert.Equal(world.Tenant, world.Scope.Entered);
        Assert.Equal(world.Clock.UtcNow, user.LastLoginAt);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task The_identifier_is_not_case_sensitive()
    {
        var world = new TestWorld();
        world.GivenUser(identifier: "awa.kone");

        var result = await world.LogIn.HandleAsync(Login(world, identifier: "  AWA.Kone "));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_wrong_pin_is_refused_and_the_attempt_is_recorded()
    {
        var world = new TestWorld();
        var user = world.GivenUser();

        var result = await world.LogIn.HandleAsync(Login(world, pin: "9999"));

        Assert.Equal(ErrorCodes.InvalidCredentials, result.Error!.Code);
        Assert.Equal(1, user.FailedAttempts);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task An_unknown_identifier_gets_the_same_answer_and_still_costs_a_verification()
    {
        var world = new TestWorld();

        var result = await world.LogIn.HandleAsync(Login(world, identifier: "nobody"));

        Assert.Equal(ErrorCodes.InvalidCredentials, result.Error!.Code);
        Assert.Equal(1, world.Hasher.DummyVerifications);
        Assert.Equal(0, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_user_of_another_tenant_cannot_log_in_here()
    {
        var world = new TestWorld();
        world.GivenUser(tenant: TenantId.New());

        var result = await world.LogIn.HandleAsync(Login(world));

        Assert.Equal(ErrorCodes.InvalidCredentials, result.Error!.Code);
    }

    [Fact]
    public async Task A_malformed_pin_is_refused_without_counting_an_attempt()
    {
        var world = new TestWorld();
        var user = world.GivenUser();

        var result = await world.LogIn.HandleAsync(Login(world, pin: "12ab"));

        Assert.Equal(ErrorCodes.InvalidCredentials, result.Error!.Code);
        Assert.Equal(0, user.FailedAttempts);
    }

    [Fact]
    public async Task Five_wrong_pins_lock_the_account_even_against_the_right_pin()
    {
        var world = new TestWorld();
        world.GivenUser();
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            await world.LogIn.HandleAsync(Login(world, pin: "0000"));
        }

        var result = await world.LogIn.HandleAsync(Login(world));

        Assert.Equal(ErrorCodes.AccountLocked, result.Error!.Code);
    }

    [Fact]
    public async Task The_lock_expires_after_fifteen_minutes()
    {
        var world = new TestWorld();
        world.GivenUser();
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            await world.LogIn.HandleAsync(Login(world, pin: "0000"));
        }

        world.Clock.UtcNow = world.Clock.UtcNow.Add(User.LockDuration);
        var result = await world.LogIn.HandleAsync(Login(world));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_disabled_user_gets_the_generic_refusal()
    {
        var world = new TestWorld();
        var user = world.GivenUser();
        user.Disable();

        var result = await world.LogIn.HandleAsync(Login(world));

        Assert.Equal(ErrorCodes.InvalidCredentials, result.Error!.Code);
    }
}
