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
        products.MapPut("/{productId:guid}/photo", (Guid productId, HttpRequest request, SetPhotoHandler handler, CancellationToken ct) =>
            SetPhotoAsync(productId, PhotoOwner.Format, request, handler, ct));
        products.MapDelete("/{productId:guid}/photo", (Guid productId, RemovePhotoHandler handler, CancellationToken ct) =>
            RemovePhotoAsync(productId, PhotoOwner.Format, handler, ct));
        api.MapGet("/stock/alerts", ListAlertsAsync);

        var families = api.MapGroup("/product-families");
        families.MapPost("", CreateFamilyAsync);
        families.MapPost("/group", GroupAsync);
        families.MapPut("/{familyId:guid}", UpdateFamilyAsync);
        families.MapPost("/{familyId:guid}/formats", AddFormatAsync);
        families.MapPut("/{familyId:guid}/photo", (Guid familyId, HttpRequest request, SetPhotoHandler handler, CancellationToken ct) =>
            SetPhotoAsync(familyId, PhotoOwner.Family, request, handler, ct));
        families.MapDelete("/{familyId:guid}/photo", (Guid familyId, RemovePhotoHandler handler, CancellationToken ct) =>
            RemovePhotoAsync(familyId, PhotoOwner.Family, handler, ct));
        api.MapGet("/photos/{photoId:guid}", GetPhotoAsync);
        return api;
    }

    private static FormatInput ToInput(FormatRequest format) =>
        new(
            new ProductId(format.ProductId),
            format.Label,
            format.Barcode,
            format.PurchasePriceFcfa,
            format.SalePriceFcfa,
            format.AlertThreshold,
            format.TracksStock);

    private static async Task<IResult> CreateFamilyAsync(
        CreateFamilyRequest request,
        CreateFamilyHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreateFamilyCommand(new FamilyId(request.FamilyId), request.Name, request.Category, request.Formats.Select(ToInput).ToArray()),
            cancellationToken);
        return result.ToHttp(created => Results.Ok(new FamilyCreatedResponse(created.FamilyId.Value, created.AlreadyProcessed)));
    }

    private static async Task<IResult> AddFormatAsync(
        Guid familyId,
        AddFormatRequest request,
        AddFormatHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new AddFormatCommand(new FamilyId(familyId), ToInput(request.Format), request.LabelForExistingFormat),
            cancellationToken);
        return result.ToHttp(id => Results.Ok(new ProductCreatedResponse(id.Value, AlreadyProcessed: false)));
    }

    private static async Task<IResult> UpdateFamilyAsync(
        Guid familyId,
        UpdateFamilyRequest request,
        UpdateFamilyHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new UpdateFamilyCommand(new FamilyId(familyId), request.Name, request.Category), cancellationToken);
        return result.ToHttp(_ => Results.NoContent());
    }

    private static async Task<IResult> GroupAsync(
        GroupIntoFamilyRequest request,
        GroupIntoFamilyHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new GroupIntoFamilyCommand(
                new FamilyId(request.FamilyId),
                request.Name,
                request.Category,
                request.Members.Select(member => new GroupMember(new ProductId(member.ProductId), member.Label)).ToArray()),
            cancellationToken);
        return result.ToHttp(id => Results.Ok(new FamilyCreatedResponse(id.Value, AlreadyProcessed: false)));
    }

    private static async Task<IResult> SetPhotoAsync(
        Guid ownerId,
        PhotoOwner owner,
        HttpRequest request,
        SetPhotoHandler handler,
        CancellationToken cancellationToken)
    {
        // On lit au plus un octet de plus que la limite : assez pour savoir que c'est trop lourd, sans charger un fichier géant en mémoire.
        using var buffer = new MemoryStream();
        var limit = SetPhotoHandler.MaxBytes + 1;
        var chunk = new byte[8192];
        int read;
        while (buffer.Length < limit && (read = await request.Body.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length)), cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        var result = await handler.HandleAsync(new SetPhotoCommand(ownerId, owner, buffer.ToArray()), cancellationToken);
        return result.ToHttp(photo => Results.Ok(new PhotoChangedResponse(photo.Value)));
    }

    private static async Task<IResult> RemovePhotoAsync(
        Guid ownerId,
        PhotoOwner owner,
        RemovePhotoHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RemovePhotoCommand(ownerId, owner), cancellationToken);
        return result.ToHttp(_ => Results.NoContent());
    }

    private static async Task<IResult> GetPhotoAsync(
        Guid photoId,
        HttpContext context,
        GetPhotoHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPhotoQuery(new PhotoId(photoId)), cancellationToken);
        return result.ToHttp(photo =>
        {
            // Une photo ne change jamais (une nouvelle photo a un nouvel identifiant) : le navigateur peut la garder, sans la partager.
            context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(photo.Data, photo.ContentType);
        });
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
                request.AlertThreshold,
                request.FormatLabel),
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
        Results.Ok(new StockChangedResponse(
            result.MovementId.Value, result.QuantityOnHand, result.AlreadyProcessed, result.PurchasePriceFcfa));
}
