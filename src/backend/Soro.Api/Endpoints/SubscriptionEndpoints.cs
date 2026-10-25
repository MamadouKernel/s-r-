using Soro.Api.Contracts;
using Soro.Application.Subscription;
using Soro.Domain.Common;

namespace Soro.Api.Endpoints;

internal static class SubscriptionEndpoints
{
    public static RouteGroupBuilder MapSubscriptionEndpoints(this RouteGroupBuilder api)
    {
        var subscription = api.MapGroup("/subscription");
        subscription.MapGet("", GetAsync);
        subscription.MapPost("/payments", DeclareAsync);
        return api;
    }

    private static async Task<IResult> GetAsync(GetSubscriptionHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        return result.ToHttp(view => Results.Ok(view));
    }

    private static async Task<IResult> DeclareAsync(
        DeclareSubscriptionPaymentRequest request,
        DeclareSubscriptionPaymentHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new DeclareSubscriptionPaymentCommand(new SubscriptionPaymentId(request.PaymentId), request.Period, request.Method, request.Reference, request.AmountFcfa),
            cancellationToken);
        return result.ToHttp(declared => Results.Ok(new SubscriptionPaymentDeclaredResponse(declared.Id.Value, declared.AlreadyProcessed)));
    }
}
