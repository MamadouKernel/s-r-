using Soro.Application.Common;
using Soro.Application.Devices;
using Soro.Domain.Common;

namespace Soro.Api.Authentication;

public static class DeviceAccessMiddleware
{
    /// <summary>
    /// Refuse toute requête dont le jeton vient d'un appareil révoqué ou supprimé (ADR 0006), même si le jeton n'a pas expiré. Les jetons sans
    /// appareil (propriétaire connecté par mot de passe) passent. À placer après <see cref="TenantScopeMiddleware"/> : le contrôle lit la
    /// table des appareils sous l'isolation du commerce.
    /// </summary>
    public static IApplicationBuilder UseDeviceAccess(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (HttpCurrentUser.ReadDevice(context.User) is { } device && HttpCurrentUser.Read(context.User) is { } identity)
            {
                var check = context.RequestServices.GetRequiredService<CheckDeviceAccessHandler>();
                if (!await check.IsAllowedAsync(device, identity.Tenant, context.RequestAborted))
                {
                    await ResultHttpMappingProblem.Write(
                        context,
                        StatusCodes.Status401Unauthorized,
                        ErrorCodes.DeviceNotAuthorized,
                        "Cet appareil n'est plus autorisé. Demandez un nouveau lien d'accès au propriétaire.");
                    return;
                }
            }

            await next();
        });
}

internal static class ResultHttpMappingProblem
{
    public static Task Write(HttpContext context, int status, string code, string detail, string title = "Appareil non autorisé") =>
        Results.Problem(
            title: title,
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
}
