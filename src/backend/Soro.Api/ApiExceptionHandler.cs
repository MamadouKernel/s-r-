using Microsoft.AspNetCore.Diagnostics;
using Soro.Infrastructure.Persistence;

namespace Soro.Api;

/// <summary>
/// Dernier filet : aucune trace de pile, de SQL ni de chemin n'est renvoyée au client (le détail va aux journaux).
/// Deux écritures concurrentes de la même clé donnent un 409 : le client renvoie sa requête, désormais acquittée.
/// </summary>
public sealed partial class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Écriture concurrente détectée sur {Path}")]
    private partial void LogConcurrentWrite(Exception exception, PathString path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erreur non gérée sur {Path}")]
    private partial void LogUnhandled(Exception exception, PathString path);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        IResult problem;
        if (exception is BadHttpRequestException bad)
        {
            // Corps illisible (JSON invalide, identifiant mal formé) : une erreur du client, jamais du serveur.
            problem = Results.Problem(
                title: "Requête invalide",
                detail: "Le contenu de la requête est illisible ou incomplet.",
                statusCode: bad.StatusCode,
                extensions: new Dictionary<string, object?> { ["code"] = "bad_request" });
        }
        else if (PersistenceErrors.IsUniqueViolation(exception) || PersistenceErrors.IsConcurrencyConflict(exception))
        {
            LogConcurrentWrite(exception, httpContext.Request.Path);
            problem = Results.Problem(
                title: "Conflit",
                detail: "Opération concurrente détectée : renvoyez la requête.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["code"] = "concurrent_conflict" });
        }
        else
        {
            LogUnhandled(exception, httpContext.Request.Path);
            problem = Results.Problem(
                title: "Erreur interne",
                detail: "Une erreur est survenue. Réessayez plus tard.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["code"] = "internal_error" });
        }

        await problem.ExecuteAsync(httpContext);
        return true;
    }
}
