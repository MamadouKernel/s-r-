using Soro.Api.Contracts;
using Soro.Application.Common;
using Soro.Application.Sales;
using Soro.Domain.Common;

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

    private static async Task<IResult> CancelAsync(
        Guid saleId,
        CancelSaleRequest request,
        CancelSaleHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CancelSaleCommand(new SaleId(saleId), request.Reason), cancellationToken);
        return result.ToHttp(_ => Results.NoContent());
    }
}
