using Soro.Api.Contracts;
using Soro.Application.Customers;
using Soro.Domain.Common;

namespace Soro.Api.Endpoints;

internal static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomerEndpoints(this RouteGroupBuilder api)
    {
        var customers = api.MapGroup("/customers");
        customers.MapGet("", ListAsync);
        customers.MapPost("", CreateAsync);
        customers.MapPut("/{customerId:guid}", UpdateAsync);
        customers.MapPost("/{customerId:guid}/deactivate", DeactivateAsync);
        customers.MapPost("/{customerId:guid}/repayments", RepayAsync);
        customers.MapGet("/{customerId:guid}/repayments", ListRepaymentsAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(
        string? search,
        bool? includeInactive,
        ListCustomersHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListCustomersQuery(search, includeInactive ?? false), cancellationToken)).ToHttp(list => Results.Ok(list));

    private static async Task<IResult> CreateAsync(CreateCustomerRequest request, CreateCustomerHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreateCustomerCommand(new CustomerId(request.CustomerId), request.Name, request.Phone, request.CreditCeilingFcfa), cancellationToken);
        return result.ToHttp(created => Results.Ok(new CustomerCreatedResponse(
            created.Customer.Id, created.AlreadyProcessed, created.Customer.CreditCeilingFcfa)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid customerId,
        UpdateCustomerRequest request,
        UpdateCustomerHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(
            new UpdateCustomerCommand(new CustomerId(customerId), request.Name, request.Phone, request.CreditCeilingFcfa), cancellationToken))
            .ToHttp(customer => Results.Ok(customer));

    private static async Task<IResult> DeactivateAsync(Guid customerId, DeactivateCustomerHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new DeactivateCustomerCommand(new CustomerId(customerId)), cancellationToken)).ToHttp(customer => Results.Ok(customer));

    private static async Task<IResult> RepayAsync(
        Guid customerId,
        RecordRepaymentRequest request,
        RecordRepaymentHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new RecordRepaymentCommand(
                new DebtRepaymentId(request.RepaymentId), new CustomerId(customerId), request.AmountFcfa, request.Method, request.Reference),
            cancellationToken);
        return result.ToHttp(done => Results.Ok(new RepaymentRecordedResponse(done.Id.Value, done.DebtFcfa, done.AlreadyProcessed)));
    }

    private static async Task<IResult> ListRepaymentsAsync(
        Guid customerId,
        int? limit,
        ListRepaymentsHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListRepaymentsQuery(new CustomerId(customerId), limit ?? 50), cancellationToken))
            .ToHttp(list => Results.Ok(list));
}
