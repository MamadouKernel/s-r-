using Npgsql;

namespace Soro.Infrastructure.Persistence;

/// <summary>
/// L'isolation entre commerces repose sur la sécurité par ligne de PostgreSQL. Un rôle super-utilisateur, ou autorisé à la contourner, la rend
/// sans effet : une erreur de configuration suffirait à montrer les données d'un commerce à un autre. Le serveur refuse donc de démarrer avec un tel
/// rôle (<c>Database:AllowPrivilegedRole=true</c> pour le permettre sciemment, par exemple le temps d'une migration).
/// </summary>
public static class DatabaseRoleGuard
{
    /// <summary>Le problème du rôle de cette connexion, ou <c>null</c> s'il est correct.</summary>
    public static async Task<string?> FindProblemAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select current_user, rolsuper, rolbypassrls from pg_roles where rolname = current_user", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return "le rôle de la connexion est introuvable.";
        }

        var role = reader.GetString(0);
        if (reader.GetBoolean(1))
        {
            return $"le rôle « {role} » est super-utilisateur : la séparation des commerces ne protégerait plus rien.";
        }

        return reader.GetBoolean(2) ? $"le rôle « {role} » peut contourner la sécurité par ligne : la séparation des commerces ne protégerait plus rien." : null;
    }
}
