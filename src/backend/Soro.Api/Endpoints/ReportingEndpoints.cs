using Soro.Application.Reporting;
using Soro.Domain.Cash;
using Soro.Domain.Common;

namespace Soro.Api.Endpoints;

/// <summary>Consultation en lecture seule (REP-01 à REP-04) : ventes, sessions de caisse et rapport d'activité.</summary>
internal static class ReportingEndpoints
{
    public static RouteGroupBuilder MapReportingEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/sales", ListSalesAsync);
        api.MapGet("/sales/{saleId:guid}", GetSaleAsync);
        api.MapGet("/register-sessions", ListSessionsAsync);
        api.MapGet("/reports/activity", GetActivityAsync);
        return api;
    }

    private static async Task<IResult> ListSalesAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? sessionId,
        Guid? cashierId,
        bool? includeCancelled,
        int? limit,
        int? offset,
        ListSalesHandler handler,
        CancellationToken cancellationToken)
    {
        var query = new ListSalesQuery(
            from,
            to,
            sessionId is { } session ? new SessionId(session) : null,
            cashierId is { } cashier ? new UserId(cashier) : null,
            includeCancelled ?? true,
            limit,
            offset);
        return (await handler.HandleAsync(query, cancellationToken)).ToHttp(sales => Results.Ok(sales));
    }

    private static async Task<IResult> GetSaleAsync(
        Guid saleId,
        GetSaleHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new SaleId(saleId), cancellationToken)).ToHttp(sale => Results.Ok(sale));

    private static async Task<IResult> ListSessionsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        SessionStatus? status,
        bool? onlyWithVariance,
        int? limit,
        int? offset,
        ListSessionsHandler handler,
        CancellationToken cancellationToken)
    {
        var query = new ListSessionsQuery(from, to, status, onlyWithVariance ?? false, limit, offset);
        return (await handler.HandleAsync(query, cancellationToken)).ToHttp(sessions => Results.Ok(sessions));
    }

    private static async Task<IResult> GetActivityAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        GetActivityReportHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new GetActivityReportQuery(from, to), cancellationToken))
            .ToHttp(report => Results.Ok(report));
}
