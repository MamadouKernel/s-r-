using Soro.Api.Contracts;
using Soro.Application.Identity;
using Soro.Domain.Common;

namespace Soro.Api.Endpoints;

internal static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        var users = api.MapGroup("/users");
        users.MapGet("", ListAsync);
        users.MapGet("/directory", DirectoryAsync);
        users.MapPost("", RegisterAsync);
        users.MapPost("/{userId:guid}/disable", DisableAsync);
        users.MapPost("/{userId:guid}/pin", ResetPinAsync);
        users.MapPost("/{userId:guid}/enable", EnableAsync);
        users.MapPut("/{userId:guid}/role", ChangeRoleAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(ListUsersHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(cancellationToken)).ToHttp(team => Results.Ok(team));

    private static async Task<IResult> DirectoryAsync(GetDirectoryHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(cancellationToken)).ToHttp(entries => Results.Ok(entries));

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

    private static async Task<IResult> ResetPinAsync(
        Guid userId,
        SetPinRequest request,
        ResetUserPinHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ResetUserPinCommand(new UserId(userId), request.Pin), cancellationToken);
        return result.ToHttp(_ => Results.NoContent());
    }

    private static async Task<IResult> EnableAsync(
        Guid userId,
        SetPinRequest request,
        EnableUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new EnableUserCommand(new UserId(userId), request.Pin), cancellationToken);
        return result.ToHttp(_ => Results.NoContent());
    }

    private static async Task<IResult> ChangeRoleAsync(
        Guid userId,
        ChangeRoleRequest request,
        ChangeUserRoleHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ChangeUserRoleCommand(new UserId(userId), request.Role), cancellationToken);
        return result.ToHttp(_ => Results.NoContent());
    }
}
