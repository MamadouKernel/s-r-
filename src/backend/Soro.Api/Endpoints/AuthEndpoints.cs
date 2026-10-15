using Soro.Api.Contracts;
using Soro.Application.Identity;
using Soro.Domain.Common;

namespace Soro.Api.Endpoints;

internal static class AuthEndpoints
{
    /// <summary>Seul point d'entrée sans jeton, donc le seul à plafond de débit strict (essais de PIN).</summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1/auth")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.LoginPolicy)
            .MapPost("/login", LogInAsync);
        return app;
    }

    private static async Task<IResult> LogInAsync(
        LogInRequest request,
        LogInHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new LogInCommand(new TenantId(request.TenantId), request.Identifier, request.Pin),
            cancellationToken);

        return result.ToHttp(login => Results.Ok(new LogInResponse(
            login.Token.Value,
            "Bearer",
            login.Token.ExpiresAt,
            new UserSummary(login.UserId.Value, login.DisplayName, login.Role))));
    }
}
