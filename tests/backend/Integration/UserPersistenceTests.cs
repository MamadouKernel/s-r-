using Microsoft.EntityFrameworkCore;
using Npgsql;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

[Collection(Postgres.Collection)]
public class UserPersistenceTests(PostgresFixture fixture)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static User NewUser(TenantId tenant, string identifier = "awa.kone", Role role = Role.Cashier) =>
        User.Register(UserId.New(), tenant, identifier, "Awa Koné", role, "pbkdf2-sha256$1$AA==$AA==", Samples.Morning);

    private async Task SaveAsync(TenantId tenant, User user)
    {
        await using var db = Db.App(fixture, tenant);
        await new UserRepository(db).AddAsync(user, Ct);
        await new EfUnitOfWork(db).CommitAsync(Ct);
    }

    [Fact]
    public async Task A_user_round_trips_with_its_login_state()
    {
        var tenant = TenantId.New();
        var user = NewUser(tenant, role: Role.Manager);
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            user.RecordFailedAttempt(Samples.Morning);
        }

        await SaveAsync(tenant, user);

        await using var db = Db.App(fixture, tenant);
        var loaded = await new UserRepository(db).GetAsync(user.Id, tenant, Ct);

        Assert.NotNull(loaded);
        Assert.Equal(user.Identifier, loaded.Identifier);
        Assert.Equal("Awa Koné", loaded.DisplayName);
        Assert.Equal(Role.Manager, loaded.Role);
        Assert.Equal(user.PinHash, loaded.PinHash);
        Assert.Equal(user.LockedUntil, loaded.LockedUntil);
        Assert.Equal(UserStatus.Active, loaded.Status);
    }

    [Fact]
    public async Task Login_progress_and_disabling_are_persisted()
    {
        var tenant = TenantId.New();
        var user = NewUser(tenant);
        await SaveAsync(tenant, user);

        await using (var db = Db.App(fixture, tenant))
        {
            var repository = new UserRepository(db);
            var loaded = (await repository.GetAsync(user.Id, tenant, Ct))!;
            loaded.RecordFailedAttempt(Samples.Morning);
            loaded.RecordFailedAttempt(Samples.Morning);
            loaded.Disable(Samples.Morning);
            await repository.UpdateAsync(loaded, Ct);
            await new EfUnitOfWork(db).CommitAsync(Ct);
        }

        await using var verify = Db.App(fixture, tenant);
        var reloaded = (await new UserRepository(verify).GetAsync(user.Id, tenant, Ct))!;
        Assert.Equal(2, reloaded.FailedAttempts);
        Assert.Equal(UserStatus.Disabled, reloaded.Status);
        Assert.Equal(Samples.Morning, reloaded.SessionsValidFrom);
    }

    [Fact]
    public async Task A_new_pin_a_new_role_and_the_session_cut_off_survive_a_round_trip()
    {
        var tenant = TenantId.New();
        var user = NewUser(tenant);
        await SaveAsync(tenant, user);

        await using (var db = Db.App(fixture, tenant))
        {
            var repository = new UserRepository(db);
            var loaded = (await repository.GetAsync(user.Id, tenant, Ct))!;
            loaded.ResetPin("hash-nouveau", Samples.Morning);
            loaded.ChangeRole(Role.StockKeeper, Samples.Morning.AddMinutes(1));
            await repository.UpdateAsync(loaded, Ct);
            await new EfUnitOfWork(db).CommitAsync(Ct);
        }

        await using var verify = Db.App(fixture, tenant);
        var reloaded = (await new UserRepository(verify).GetAsync(user.Id, tenant, Ct))!;
        Assert.Equal("hash-nouveau", reloaded.PinHash);
        Assert.Equal(Role.StockKeeper, reloaded.Role);
        Assert.Equal(Samples.Morning.AddMinutes(1), reloaded.SessionsValidFrom);
    }

    [Fact]
    public async Task A_user_is_found_by_its_normalized_identifier()
    {
        var tenant = TenantId.New();
        var user = NewUser(tenant, "Awa.Kone");
        await SaveAsync(tenant, user);

        await using var db = Db.App(fixture, tenant);
        var found = await new UserRepository(db).FindByIdentifierAsync(tenant, User.Normalize(" AWA.kone "), Ct);

        Assert.Equal(user.Id, found!.Id);
    }

    [Fact]
    public async Task An_identifier_is_unique_per_commerce_but_reusable_across_commerces()
    {
        var tenant = TenantId.New();
        await SaveAsync(tenant, NewUser(tenant));

        var other = TenantId.New();
        await SaveAsync(other, NewUser(other));

        await using var db = Db.App(fixture, tenant);
        await new UserRepository(db).AddAsync(NewUser(tenant), Ct);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => new EfUnitOfWork(db).CommitAsync(Ct));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [Fact]
    public async Task Another_commerce_cannot_see_the_user()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var user = NewUser(tenantA);
        await SaveAsync(tenantA, user);

        await using var asB = Db.App(fixture, tenantB);
        var repository = new UserRepository(asB);

        Assert.Null(await repository.GetAsync(user.Id, tenantA, Ct));
        Assert.Null(await repository.FindByIdentifierAsync(tenantA, user.Identifier, Ct));
    }

    [Fact]
    public async Task A_user_cannot_be_deleted_even_by_the_schema_owner()
    {
        var tenant = TenantId.New();
        await SaveAsync(tenant, NewUser(tenant));
        await using var owner = Db.Owner(fixture);

        await Assert.ThrowsAsync<PostgresException>(() =>
            owner.Database.ExecuteSqlRawAsync("DELETE FROM users", Ct));
    }

    [Fact]
    public async Task The_directory_knows_the_role_of_active_users_only()
    {
        var tenant = TenantId.New();
        var manager = NewUser(tenant, "manager.one", Role.Manager);
        var leaver = NewUser(tenant, "leaver.one", Role.Cashier);
        leaver.Disable(Samples.Morning);
        await SaveAsync(tenant, manager);
        await SaveAsync(tenant, leaver);

        await using var db = Db.App(fixture, tenant);
        var directory = new UserDirectory(db);

        Assert.Equal(Role.Manager, await directory.GetRoleAsync(manager.Id, tenant, Ct));
        Assert.Null(await directory.GetRoleAsync(leaver.Id, tenant, Ct));
        Assert.Null(await directory.GetRoleAsync(manager.Id, TenantId.New(), Ct));
    }

    [Fact]
    public async Task A_tenant_scope_entered_after_the_context_exists_is_honoured()
    {
        var tenant = TenantId.New();
        var user = NewUser(tenant);
        await SaveAsync(tenant, user);
        var scope = new TenantContext();
        await using var db = new SoroDbContext(PersistenceOptions.Create(fixture.AppConnectionString("scope-test"), scope));
        var repository = new UserRepository(db);

        var beforeEntering = await repository.FindByIdentifierAsync(tenant, user.Identifier, Ct);
        scope.Enter(tenant);
        var afterEntering = await repository.FindByIdentifierAsync(tenant, user.Identifier, Ct);

        Assert.Null(beforeEntering);
        Assert.NotNull(afterEntering);
    }
}
