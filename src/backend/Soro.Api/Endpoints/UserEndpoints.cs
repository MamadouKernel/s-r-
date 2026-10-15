using Soro.Api.Contracts;
using Soro.Application.Identity;
using Soro.Domain.Common;

namespace Soro.Api.Endpoints;

internal static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        var users = api.MapGroup("/users");
        users.MapPost("", RegisterAsync);
        users.MapPost("/{userId:guid}/disable", DisableAsync);
        return api;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterUserRequest request,
        RegisterUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new RegisterUserCommand(
                new UserId(request.UserId), request.Identifier, request.DisplayName, request.Role, request.Pin),
            cancellationToken);
        return result.ToHttp(registered => Results.Ok(
            new UserRegisteredResponse(registered.UserId.Value, registered.AlreadyProcessed)));
    }

    private static async Task<IResult> DisableAsync(
        Guid userId,
        DisableUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DisableUserCommand(new UserId(userId)), cancellationToken);
        return result.ToHttp(_ => Results.NoContent());
    }
}
