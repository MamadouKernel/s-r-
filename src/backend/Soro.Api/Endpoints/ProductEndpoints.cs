using Soro.Api.Contracts;
using Soro.Application.Stock;
using Soro.Domain.Common;

namespace Soro.Api.Endpoints;

internal static class ProductEndpoints
{
    public static RouteGroupBuilder MapProductEndpoints(this RouteGroupBuilder api)
    {
        var products = api.MapGroup("/products");
        products.MapGet("", ListAsync);
        products.MapPost("", CreateAsync);
        products.MapPut("/{productId:guid}", UpdateAsync);
        products.MapPost("/{productId:guid}/deactivate", DeactivateAsync);
        products.MapPost("/{productId:guid}/receptions", ReceiveAsync);
        products.MapPost("/{productId:guid}/losses", RecordLossAsync);
        products.MapGet("/{productId:guid}/movements", ListMovementsAsync);
        api.MapGet("/stock/alerts", ListAlertsAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(
        string? search,
        bool? includeInactive,
        ListProductsHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(new ListProductsQuery(search, includeInactive ?? false), cancellationToken));

    private static async Task<IResult> CreateAsync(
        CreateProductRequest request,
        CreateProductHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreateProductCommand(
                new ProductId(request.ProductId),
                request.Name,
                request.Barcode,
                request.Category,
                request.PurchasePriceFcfa,
                request.SalePriceFcfa,
                request.AlertThreshold,
                request.TracksStock),
            cancellationToken);
        return result.ToHttp(created => Results.Ok(new ProductCreatedResponse(created.ProductId.Value, created.AlreadyProcessed)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid productId,
        UpdateProductRequest request,
        UpdateProductHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new UpdateProductCommand(
                new ProductId(productId),
                request.Name,
                request.Barcode,
                request.Category,
                request.PurchasePriceFcfa,
                request.SalePriceFcfa,
                request.AlertThreshold),
            cancellationToken);
        return result.ToHttp(_ => Results.NoContent());
    }

    private static async Task<IResult> DeactivateAsync(
        Guid productId,
        DeactivateProductHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeactivateProductCommand(new ProductId(productId)), cancellationToken);
        return result.ToHttp(_ => Results.NoContent());
    }

    private static async Task<IResult> ReceiveAsync(
        Guid productId,
        ReceiveStockRequest request,
        ReceiveStockHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new ReceiveStockCommand(
                new StockMovementId(request.MovementId),
                new ProductId(productId),
                request.Quantity,
                request.Supplier,
                request.PurchasePriceFcfa),
            cancellationToken);
        return result.ToHttp(ToResponse);
    }

    private static async Task<IResult> RecordLossAsync(
        Guid productId,
        RecordStockLossRequest request,
        RecordStockLossHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new RecordStockLossCommand(
                new StockMovementId(request.MovementId),
                new ProductId(productId),
                request.Quantity,
                request.Reason),
            cancellationToken);
        return result.ToHttp(ToResponse);
    }

    private static async Task<IResult> ListMovementsAsync(
        Guid productId,
        int? limit,
        ListStockMovementsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new ListStockMovementsQuery(new ProductId(productId), limit ?? 50),
            cancellationToken);
        return result.ToHttp(movements => Results.Ok(movements));
    }

    private static async Task<IResult> ListAlertsAsync(
        ListLowStockHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        return result.ToHttp(products => Results.Ok(products));
    }

    private static IResult ToResponse(StockResult result) =>
        Results.Ok(new StockChangedResponse(result.MovementId.Value, result.QuantityOnHand, result.AlreadyProcessed));
}
