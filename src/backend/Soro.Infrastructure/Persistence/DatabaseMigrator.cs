using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Soro.Infrastructure.Persistence;

/// <summary>
/// Applique les migrations de la base (docs/specs/exploitation EXP-01). C'est une étape de déploiement, à part : l'API ne le fait jamais au démarrage.
/// Deux exécutions simultanées sont sûres : un verrou consultatif PostgreSQL les met à la file, la seconde ne trouve alors plus rien à faire.
/// </summary>
public static class DatabaseMigrator
{
    // Clé arbitraire mais fixe, propre à Sôrô : toutes les exécutions réclament le même verrou.
    private const long LockKey = 5_240_713_001;

    /// <summary>Les migrations que la base n'a pas encore, dans l'ordre. Une base injoignable lève l'exception : le déploiement doit s'arrêter.</summary>
    public static async Task<IReadOnlyList<string>> PendingAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var db = new SoroDbContext(PersistenceOptions.Create(connectionString));
        return (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
    }

    /// <summary>Applique tout ce qui manque et rend les noms des migrations appliquées par cet appel.</summary>
    public static async Task<IReadOnlyList<string>> MigrateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        // Le verrou appartient à cette connexion : il tombe tout seul si l'outil est interrompu, mais une connexion rendue au pool reste ouverte,
        // d'où le relâchement explicite.
        await using var locker = new NpgsqlConnection(connectionString);
        await locker.OpenAsync(cancellationToken);
        await using (var acquire = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", locker))
        {
            acquire.Parameters.AddWithValue("key", LockKey);
            await acquire.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var db = new SoroDbContext(PersistenceOptions.Create(connectionString));
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            await db.Database.MigrateAsync(cancellationToken);
            return pending;
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", locker);
            release.Parameters.AddWithValue("key", LockKey);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
