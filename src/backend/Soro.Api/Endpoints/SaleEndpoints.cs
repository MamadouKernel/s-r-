using Soro.Api.Contracts;
using Soro.Application.Common;
using Soro.Application.Sales;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Sales;

namespace Soro.Api.Endpoints;

internal static class SaleEndpoints
{
    /// <summary>Borne un lot : une caisse longtemps hors ligne envoie plusieurs lots plutôt qu'un lot géant.</summary>
    private const int MaxSyncBatchSize = 200;

    public static RouteGroupBuilder MapSaleEndpoints(this RouteGroupBuilder api)
    {
        var sales = api.MapGroup("/sales");
        sales.MapPost("", RecordAsync);
        sales.MapPost("/sync", SyncAsync);
        sales.MapPost("/{saleId:guid}/cancel", CancelAsync);
        sales.MapPost("/{saleId:guid}/returns", ReturnAsync);
        api.MapPost("/approvals", GrantApprovalAsync);
        return api;
    }

    private static async Task<IResult> RecordAsync(
        RecordSaleRequest request,
        RecordSaleHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);
        return result.ToHttp(recorded => Results.Ok(new SaleRecordedResponse(
            recorded.SaleId.Value, recorded.NetTotalFcfa, recorded.ChangeFcfa, recorded.AlreadyProcessed)));
    }

    private static async Task<IResult> SyncAsync(
        SyncSalesRequest request,
        SyncSalesHandler handler,
        CancellationToken cancellationToken)
    {
        if (request.Sales.Count > MaxSyncBatchSize)
        {
            return ResultHttpMapping.ToProblem(
                new ApplicationError(ErrorCodes.RuleViolation, $"Un lot contient au plus {MaxSyncBatchSize} ventes."));
        }

        var result = await handler.HandleAsync(
            new SyncSalesCommand(request.Sales.Select(sale => sale.ToCommand()).ToArray()),
            cancellationToken);

        return Results.Ok(new SyncSalesResponse(result.Statuses
            .Select(status => new SaleSyncStatusResponse(
                status.SaleId.Value, status.Outcome.ToString(), status.ErrorCode, status.Message))
            .ToArray()));
    }

    /// <summary>Un gérant tape son identifiant et son PIN sur la caisse ; la caisse reçoit une approbation signée pour cette vente seulement.</summary>
    private static async Task<IResult> GrantApprovalAsync(GrantApprovalRequest request, GrantApprovalHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GrantApprovalCommand(request.Identifier, request.Pin, request.SubjectId, request.Purpose, request.Amount), cancellationToken);
        return result.ToHttp(granted => Results.Ok(new ApprovalGrantedResponse(granted.Token, granted.Approver.Value, granted.ApproverName, granted.ExpiresAt)));
    }

    private static async Task<IResult> CancelAsync(
        Guid saleId,
        CancelSaleRequest request,
        CancelSaleHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CancelSaleCommand(
            new SaleId(saleId),
            request.Reason,
            request.OperationId is { } operation ? new SaleReturnId(operation) : null,
            request.RegisterId is { } register ? new RegisterId(register) : null,
            request.Resellable,
            Approve(request.ApproverId, request.ApproverPin, request.ApprovalToken));
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.ToHttp(done => Results.Ok(ToResponse(done)));
    }

    private static async Task<IResult> ReturnAsync(
        Guid saleId,
        ReturnSaleRequest request,
        ReturnSaleHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new ReturnSaleCommand(
            new SaleId(saleId),
            new SaleReturnId(request.OperationId),
            request.Lines.Select(line => new ReturnRequest(line.LineIndex, line.Quantity, line.Resellable)).ToArray(),
            request.Reason,
            request.RegisterId is { } register ? new RegisterId(register) : null,
            Approve(request.ApproverId, request.ApproverPin, request.ApprovalToken));
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.ToHttp(done => Results.Ok(ToResponse(done)));
    }

    private static Approval? Approve(Guid? approver, string? pin, string? token) =>
        token is not null ? Approval.FromToken(token) : approver is { } id ? new Approval(new UserId(id), pin ?? string.Empty) : null;

    private static ReversalResponse ToResponse(ReversalResult done) =>
        new(done.Id.Value, done.TotalFcfa, done.CashFcfa, done.CreditFcfa, done.Refund.Select(part => new RefundPartResponse(part.Method, part.Amount.Amount)).ToArray(), done.AlreadyProcessed);
}
