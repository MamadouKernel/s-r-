using Npgsql;

namespace Soro.Api.Endpoints;

/// <summary>
/// Points de santé (docs/specs/exploitation EXP-05), lus par la surveillance extérieure : sans compte, sans donnée.
/// <c>live</c> : le serveur tourne. <c>ready</c> : il sait aussi parler à sa base. Aucune version, aucune adresse, aucun détail d'erreur.
/// </summary>
internal static partial class HealthEndpoints
{
    private static readonly TimeSpan DatabaseTimeout = TimeSpan.FromSeconds(3);

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        var health = app.MapGroup("/health").AllowAnonymous();
        health.MapGet("/live", () => Results.Text("ok"));
        health.MapGet("/ready", (ILoggerFactory loggers, CancellationToken cancellationToken) => ReadyAsync(connectionString, loggers.CreateLogger("Health"), cancellationToken));
        return app;
    }

    private static async Task<IResult> ReadyAsync(string connectionString, ILogger logger, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DatabaseTimeout);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(timeout.Token);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(timeout.Token);
            return Results.Text("ok");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Le type seulement : le message d'une base injoignable peut contenir une adresse.
            LogDatabaseDown(logger, exception.GetType().Name);
            return Results.Text("indisponible", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "La base ne répond pas au point de santé ({ExceptionType}).")]
    private static partial void LogDatabaseDown(ILogger logger, string exceptionType);
}
