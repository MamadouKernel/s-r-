using Soro.Infrastructure.Persistence;

namespace Soro.Api;

/// <summary>Au démarrage, refuse un rôle de base qui contourne la séparation des commerces (<c>Database:AllowPrivilegedRole=true</c> pour l'autoriser sciemment).</summary>
public sealed class DatabaseRoleCheck(string connectionString, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration.GetValue("Database:AllowPrivilegedRole", false))
        {
            return;
        }

        if (await DatabaseRoleGuard.FindProblemAsync(connectionString, cancellationToken) is { } problem)
        {
            throw new InvalidOperationException($"Connexion à la base refusée : {problem} Utilisez un rôle sans ces droits.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
