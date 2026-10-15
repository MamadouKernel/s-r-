using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Soro.Infrastructure.Persistence;

/// <summary>
/// À chaque ouverture de connexion, pose le commerce courant dans <c>app.tenant_id</c>, que lisent les
/// politiques RLS. Le réglage vit au niveau session : Npgsql le réinitialise au retour dans le pool
/// (DISCARD ALL), il ne peut donc pas passer d'un commerce à l'autre. Sans commerce connu, rien n'est posé
/// et la base n'expose aucune ligne.
/// </summary>
public sealed class TenantSessionInterceptor(ITenantAccessor tenant) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (CreateCommand(connection) is { } command)
        {
            using (command)
            {
                command.ExecuteNonQuery();
            }
        }
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (CreateCommand(connection) is { } command)
        {
            await using (command)
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private DbCommand? CreateCommand(DbConnection connection)
    {
        if (tenant.Current is not { } current)
        {
            return null;
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = current.Value.ToString();
        command.Parameters.Add(parameter);
        return command;
    }
}
