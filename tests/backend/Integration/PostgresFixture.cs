using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Soro.Integration.Tests;

/// <summary>
/// Un PostgreSQL jetable dans Docker, migré comme en production. Les tests se connectent avec un rôle
/// applicatif sans privilège (ni superutilisateur, ni BYPASSRLS) pour que l'isolation soit réellement vérifiée.
/// L'image se choisit avec SORO_TEST_POSTGRES_IMAGE (la CI doit utiliser postgres:18, version cible).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string AppRole = "soro_app";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(
        Environment.GetEnvironmentVariable("SORO_TEST_POSTGRES_IMAGE") ?? "postgres:17").Build();

    private string _appPassword = string.Empty;

    public string OwnerConnectionString => _container.GetConnectionString();

    /// <summary>Chaîne du rôle applicatif ; un nom d'application distinct donne un pool distinct.</summary>
    public string AppConnectionString(string applicationName = "soro-tests", int maxPoolSize = 20) =>
        new NpgsqlConnectionStringBuilder(OwnerConnectionString)
        {
            Username = AppRole,
            Password = _appPassword,
            ApplicationName = applicationName,
            MaxPoolSize = maxPoolSize,
        }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using (var owner = Db.Owner(this))
        {
            await owner.Database.MigrateAsync();
        }

        _appPassword = Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Mot de passe aléatoire généré ici, conteneur éphémère.
        command.CommandText =
            $"CREATE ROLE {AppRole} LOGIN PASSWORD '{_appPassword}' NOSUPERUSER NOBYPASSRLS;"
            + $"GRANT USAGE ON SCHEMA public TO {AppRole};"
            + $"GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA public TO {AppRole};";
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Postgres.Collection)]
public sealed class PostgresSharedFixture : ICollectionFixture<PostgresFixture>;

public static class Postgres
{
    public const string Collection = "postgres";
}
