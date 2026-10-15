using Soro.Api.Contracts;
using Soro.Application.Cash;
using Soro.Domain.Common;

namespace Soro.Api.Endpoints;

internal static class RegisterSessionEndpoints
{
    public static RouteGroupBuilder MapRegisterSessionEndpoints(this RouteGroupBuilder api)
    {
        var sessions = api.MapGroup("/register-sessions");
        sessions.MapPost("", OpenAsync);
        sessions.MapPost("/{sessionId:guid}/close", CloseAsync);
        return api;
    }

    private static async Task<IResult> OpenAsync(
        OpenRegisterSessionRequest request,
        OpenRegisterSessionHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);
        return result.ToHttp(opened => Results.Ok(new SessionOpenedResponse(opened.SessionId.Value, opened.AlreadyProcessed)));
    }

    private static async Task<IResult> CloseAsync(
        Guid sessionId,
        CloseRegisterSessionRequest request,
        CloseRegisterSessionHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CloseRegisterSessionCommand(new SessionId(sessionId), request.CountedCashFcfa, request.At);
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.ToHttp(closed => Results.Ok(new SessionClosedResponse(
            closed.ExpectedCashFcfa, closed.CountedCashFcfa, closed.VarianceFcfa, closed.AlreadyProcessed)));
    }
}
