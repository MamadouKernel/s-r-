using Soro.Application.Export;

namespace Soro.Api.Endpoints;

internal static class DataExportEndpoints
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static RouteGroupBuilder MapDataExportEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/data-export", ExportAsync);
        return api;
    }

    private static async Task<IResult> ExportAsync(DateOnly? from, DateOnly? to, ExportDataHandler handler, HttpContext context, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ExportDataQuery(from, to), cancellationToken);
        return result.ToHttp(file =>
        {
            // Le fichier contient les données de la boutique : jamais gardé dans un cache, ni partagé ni du navigateur.
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(file.Bytes, XlsxContentType, file.FileName);
        });
    }
}
