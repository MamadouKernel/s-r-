using Soro.Api.Contracts;
using Soro.Application.Cash;
using Soro.Application.Sales;
using Soro.Domain.Common;

namespace Soro.Api.Endpoints;

internal static class RegisterSessionEndpoints
{
    public static RouteGroupBuilder MapRegisterSessionEndpoints(this RouteGroupBuilder api)
    {
        var sessions = api.MapGroup("/register-sessions");
        sessions.MapPost("", OpenAsync);
        sessions.MapPost("/{sessionId:guid}/close", CloseAsync);
        sessions.MapPost("/{sessionId:guid}/movements", RecordMovementAsync);
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
        var command = new CloseRegisterSessionCommand(new SessionId(sessionId), request.CountedCashFcfa, request.At, request.Recount, request.Denominations);
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.ToHttp(closed => Results.Ok(new SessionClosedResponse(
            closed.Closed, closed.NeedsRecount, closed.CountedCashFcfa, closed.ExpectedCashFcfa, closed.VarianceFcfa, closed.AlreadyProcessed)));
    }

    /// <summary>Une dépense, un versement ou un apport d'espèces sur la session ouverte (docs/specs/cloture-sorties-rapport CLO-01).</summary>
    private static async Task<IResult> RecordMovementAsync(
        Guid sessionId,
        CashMovementRequest request,
        RecordCashMovementHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new RecordCashMovementCommand(
            new SessionId(sessionId),
            new CashMovementId(request.MovementId),
            request.Kind,
            request.AmountFcfa,
            request.Reason,
            request.At,
            request.ApprovalToken is { } token ? Approval.FromToken(token) : null);
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.ToHttp(done => Results.Ok(new CashMovementRecordedResponse(done.Id.Value, done.AlreadyProcessed)));
    }
}
