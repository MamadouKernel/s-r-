using Soro.Api.Contracts;
using Soro.Application.Signup;
using Soro.Domain.Identity;

namespace Soro.Api.Endpoints;

internal static class SignupEndpoints
{
    /// <summary>
    /// Inscription et mot de passe oublié : publics (le commerce n'existe pas encore, ou le propriétaire ne peut plus se connecter),
    /// donc tous derrière le plafond de débit par adresse. Les plafonds par numéro sont dans les cas d'usage.
    /// </summary>
    public static IEndpointRouteBuilder MapSignupEndpoints(this IEndpointRouteBuilder app)
    {
        var signup = app.MapGroup("/api/v1/signup").AllowAnonymous().RequireRateLimiting(RateLimitingSetup.PublicPolicy);
        signup.MapPost("/start", StartAsync);
        signup.MapPost("/resend", ResendAsync);
        signup.MapPost("/complete", CompleteAsync);

        var reset = app.MapGroup("/api/v1/owner/password-reset").AllowAnonymous().RequireRateLimiting(RateLimitingSetup.PublicPolicy);
        reset.MapPost("/start", StartResetAsync);
        reset.MapPost("/confirm", ConfirmResetAsync);

        app.MapGroup("/api/v1/auth/owner").AllowAnonymous().RequireRateLimiting(RateLimitingSetup.LoginPolicy)
            .MapPost("/login", LogInAsync);
        return app;
    }

    private static async Task<IResult> StartAsync(StartSignupRequest request, StartSignupHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new StartSignupCommand(request.Phone, request.CommerceName, request.Activity, request.OwnerName, request.TermsVersion),
            cancellationToken);
        return result.ToHttp(started => Results.Ok(new SignupStartedResponse(started.VerificationId, started.ExpiresAt, started.ResendAvailableAt)));
    }

    private static async Task<IResult> ResendAsync(ResendSignupCodeRequest request, ResendSignupCodeHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ResendSignupCodeCommand(request.VerificationId), cancellationToken);
        return result.ToHttp(started => Results.Ok(new SignupStartedResponse(started.VerificationId, started.ExpiresAt, started.ResendAvailableAt)));
    }

    private static async Task<IResult> CompleteAsync(CompleteSignupRequest request, CompleteSignupHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CompleteSignupCommand(request.VerificationId, request.Code, request.Password), cancellationToken);
        return result.ToHttp(done => Results.Ok(new OwnerSessionResponse(
            done.Token.Value,
            "Bearer",
            done.Token.ExpiresAt,
            new UserSummary(done.UserId.Value, done.DisplayName, Role.Owner),
            done.Tenant.Value,
            done.CommerceName,
            done.TrialEndsAt)));
    }

    private static async Task<IResult> LogInAsync(OwnerLogInRequest request, OwnerLogInHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new OwnerLogInCommand(request.Phone, request.Password), cancellationToken);
        return result.ToHttp(login => Results.Ok(new OwnerSessionResponse(
            login.Token.Value,
            "Bearer",
            login.Token.ExpiresAt,
            new UserSummary(login.UserId.Value, login.DisplayName, Role.Owner),
            login.Tenant.Value,
            login.CommerceName,
            null)));
    }

    private static async Task<IResult> StartResetAsync(StartPasswordResetRequest request, StartPasswordResetHandler handler, CancellationToken cancellationToken)
    {
        // 202 que le numéro ait un compte ou non : la réponse ne révèle rien (SC-I9).
        var result = await handler.HandleAsync(new StartPasswordResetCommand(request.Phone), cancellationToken);
        return result.ToHttp(_ => Results.Accepted());
    }

    private static async Task<IResult> ConfirmResetAsync(ConfirmPasswordResetRequest request, ConfirmPasswordResetHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new ConfirmPasswordResetCommand(request.Phone, request.Code, request.NewPassword), cancellationToken);
        return result.ToHttp(_ => Results.NoContent());
    }
}
