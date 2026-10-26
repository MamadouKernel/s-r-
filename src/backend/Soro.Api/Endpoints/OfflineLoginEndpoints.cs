using Soro.Api.Contracts;
using Soro.Application.Identity;
using Soro.Domain.Common;

namespace Soro.Api.Endpoints;

internal static class OfflineLoginEndpoints
{
    /// <summary>Les connexions sans réseau notées sur l'appareil, envoyées avec la file au retour du réseau (docs/specs/connexion-hors-ligne HLC-10).</summary>
    public static RouteGroupBuilder MapOfflineLoginEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/offline-logins", RecordAsync);
        return api;
    }

    private static async Task<IResult> RecordAsync(OfflineLoginsRequest request, RecordOfflineLoginsHandler handler, CancellationToken cancellationToken)
    {
        var command = new RecordOfflineLoginsCommand(
            request.Logins.Select(login => new OfflineLoginInput(login.EventId, new UserId(login.UserId), login.StartedAt, login.EndedAt)).ToArray());
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.ToHttp(recorded => Results.Ok(new OfflineLoginsRecordedResponse(recorded)));
    }
}
