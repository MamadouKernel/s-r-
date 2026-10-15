using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Soro.Application.Identity;

namespace Soro.Infrastructure.Persistence;

/// <summary>
/// À chaque ouverture de connexion, pose le commerce courant dans <c>app.tenant_id</c>, que lisent les
/// politiques RLS. Le réglage vit au niveau session : Npgsql le réinitialise au retour dans le pool
/// (DISCARD ALL), il ne peut donc pas passer d'un commerce à l'autre.
/// </summary>
public sealed class TenantSessionInterceptor(ICurrentUser currentUser) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";

        var tenant = command.CreateParameter();
        tenant.ParameterName = "tenant";
        tenant.Value = currentUser.Tenant.Value.ToString();
        command.Parameters.Add(tenant);
        return command;
    }
}
