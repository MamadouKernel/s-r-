using Microsoft.EntityFrameworkCore;
using Npgsql;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

/// <summary>Les migrations appliquées par une étape à part (docs/specs/exploitation EXP-01), jamais par l'API au démarrage.</summary>
[Collection(Postgres.Collection)]
public sealed class DatabaseMigratorTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_new_database_receives_every_migration_and_a_second_run_changes_nothing()
    {
        await using var database = await TemporaryDatabase.CreateAsync(fixture);

        var first = await DatabaseMigrator.MigrateAsync(database.ConnectionString);
        var second = await DatabaseMigrator.MigrateAsync(database.ConnectionString);

        Assert.NotEmpty(first);
        Assert.Empty(second);
        await using var db = new SoroDbContext(PersistenceOptions.Create(database.ConnectionString));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Two_runs_at_the_same_time_both_finish_and_leave_one_complete_schema()
    {
        await using var database = await TemporaryDatabase.CreateAsync(fixture);

        await Task.WhenAll(DatabaseMigrator.MigrateAsync(database.ConnectionString), DatabaseMigrator.MigrateAsync(database.ConnectionString));

        await using var db = new SoroDbContext(PersistenceOptions.Create(database.ConnectionString));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(
            (await db.Database.GetAppliedMigrationsAsync()).Count(),
            db.Database.GetMigrations().Count());
    }

    [Fact]
    public async Task An_unreachable_database_is_reported_as_a_failure_so_the_deployment_stops()
    {
        var unreachable = new NpgsqlConnectionStringBuilder(fixture.OwnerConnectionString) { Host = "127.0.0.1", Port = 1, Timeout = 2 }.ConnectionString;

        await Assert.ThrowsAnyAsync<Exception>(() => DatabaseMigrator.MigrateAsync(unreachable));
    }

    [Fact]
    public async Task A_database_already_up_to_date_for_the_running_version_reports_nothing_to_apply()
    {
        await using var database = await TemporaryDatabase.CreateAsync(fixture);
        await DatabaseMigrator.MigrateAsync(database.ConnectionString);

        Assert.Empty(await DatabaseMigrator.PendingAsync(database.ConnectionString));
    }
}

internal sealed class TemporaryDatabase : IAsyncDisposable
{
    private readonly string _owner;
    private readonly string _name;

    private TemporaryDatabase(string owner, string name, string connectionString)
    {
        _owner = owner;
        _name = name;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<TemporaryDatabase> CreateAsync(PostgresFixture fixture)
    {
        var name = $"soro_migrator_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(fixture.OwnerConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
        await create.ExecuteNonQueryAsync();
        return new TemporaryDatabase(fixture.OwnerConnectionString, name, new NpgsqlConnectionStringBuilder(fixture.OwnerConnectionString) { Database = name }.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_owner);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
