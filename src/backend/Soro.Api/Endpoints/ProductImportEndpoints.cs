using Soro.Application.Stock;

namespace Soro.Api.Endpoints;

internal static class ProductImportEndpoints
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static RouteGroupBuilder MapProductImportEndpoints(this RouteGroupBuilder api)
    {
        var imports = api.MapGroup("/product-imports");
        imports.MapGet("/template", TemplateAsync);
        imports.MapPost("/preview", PreviewAsync);
        imports.MapPost("", ImportAsync);
        return api;
    }

    private static async Task<IResult> TemplateAsync(GetImportTemplateHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        return result.ToHttp(bytes => Results.File(bytes, XlsxContentType, "modele-import-produits.xlsx"));
    }

    private static async Task<IResult> PreviewAsync(HttpRequest request, PreviewProductImportHandler handler, CancellationToken cancellationToken)
    {
        var file = await BodyReader.ReadAtMostAsync(request.Body, ProductImportLimits.MaxBytes, cancellationToken);
        var result = await handler.HandleAsync(new ProductImportQuery(file), cancellationToken);
        return result.ToHttp(report => Results.Ok(report));
    }

    private static async Task<IResult> ImportAsync(Guid? importId, HttpRequest request, ImportProductsHandler handler, CancellationToken cancellationToken)
    {
        if (importId is null || importId == Guid.Empty)
        {
            return Results.Problem(detail: "L'identifiant de l'importation est obligatoire.", statusCode: StatusCodes.Status400BadRequest);
        }

        var file = await BodyReader.ReadAtMostAsync(request.Body, ProductImportLimits.MaxBytes, cancellationToken);
        var result = await handler.HandleAsync(new ImportProductsCommand(importId.Value, file), cancellationToken);
        // Des lignes à corriger : rien n'a été créé. 422 avec le compte rendu, pour que l'écran liste les lignes.
        return result.ToHttp(report => report.ProblemCount > 0 ? Results.Json(report, statusCode: StatusCodes.Status422UnprocessableEntity) : Results.Ok(report));
    }
}

/// <summary>Lit un corps de requête sans jamais en charger plus que la limite (plus un octet, pour savoir qu'elle est dépassée).</summary>
internal static class BodyReader
{
    public static async Task<byte[]> ReadAtMostAsync(Stream body, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var max = limit + 1;
        int read;
        while (buffer.Length < max
            && (read = await body.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, max - buffer.Length)), cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
