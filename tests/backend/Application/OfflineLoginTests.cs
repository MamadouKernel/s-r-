using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Reporting;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

public class OfflineLoginTests
{
    private static readonly DateTimeOffset Start = TestWorld.Morning.AddMinutes(2);

    private static OfflineLoginInput Login(UserId user, DateTimeOffset? start = null, DateTimeOffset? end = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), user, start ?? Start, end ?? Start.AddHours(6));

    [Fact]
    public async Task A_login_without_network_is_recorded_in_the_journal_under_the_account_that_made_it()
    {
        var world = new TestWorld();
        var input = Login(world.Cashier);

        var result = await world.RecordOfflineLogins.HandleAsync(new RecordOfflineLoginsCommand([input]));

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.LoginOffline, entry.Action);
        Assert.Equal(world.Cashier, entry.User);
        Assert.Equal(input.EventId, entry.ResourceId);
        Assert.Contains("sans réseau", entry.Detail, StringComparison.Ordinal);
        Assert.Contains("08:02", entry.Detail, StringComparison.Ordinal);
        Assert.Contains("14:02", entry.Detail, StringComparison.Ordinal);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Sending_the_same_event_twice_records_it_once()
    {
        var world = new TestWorld();
        var input = Login(world.Cashier);

        await world.RecordOfflineLogins.HandleAsync(new RecordOfflineLoginsCommand([input]));
        var again = await world.RecordOfflineLogins.HandleAsync(new RecordOfflineLoginsCommand([input]));

        Assert.True(again.IsSuccess);
        Assert.Single(world.Audit.Entries);
    }

    [Fact]
    public async Task A_manager_can_send_the_events_of_a_cashier_but_an_unknown_or_other_role_account_is_ignored()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var stranger = UserId.New();
        world.Directory.Set(world.Tenant, world.Owner, Role.Owner);

        var result = await world.RecordOfflineLogins.HandleAsync(new RecordOfflineLoginsCommand(
            [Login(world.Cashier), Login(stranger), Login(world.Owner)]));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        Assert.Equal(world.Cashier, Assert.Single(world.Audit.Entries).User);
    }

    [Fact]
    public async Task A_device_clock_in_the_future_is_flagged_in_the_detail()
    {
        var world = new TestWorld();
        var future = world.Clock.UtcNow.AddDays(3);

        await world.RecordOfflineLogins.HandleAsync(new RecordOfflineLoginsCommand([Login(world.Cashier, future, future.AddHours(1))]));

        Assert.Contains("heure de l'appareil", Assert.Single(world.Audit.Entries).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_event_that_ends_before_it_starts_is_flagged_too()
    {
        var world = new TestWorld();

        await world.RecordOfflineLogins.HandleAsync(new RecordOfflineLoginsCommand([Login(world.Cashier, Start, Start.AddHours(-2))]));

        Assert.Contains("heure de l'appareil", Assert.Single(world.Audit.Entries).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task More_than_a_hundred_events_at_once_are_refused()
    {
        var world = new TestWorld();

        var result = await world.RecordOfflineLogins.HandleAsync(new RecordOfflineLoginsCommand(
            Enumerable.Range(0, 101).Select(_ => Login(world.Cashier)).ToArray()));

        Assert.False(result.IsSuccess);
        Assert.Empty(world.Audit.Entries);
    }

    [Fact]
    public async Task The_journal_groups_these_events_under_security()
    {
        Assert.True(AuditCategories.TryGetPrefixes("security", out var prefixes));
        Assert.Contains(AuditActions.LoginOffline, prefixes.Select(prefix => prefix + "offline"));
        await Task.CompletedTask;
    }
}
