using Soro.Application.Identity;

namespace Soro.Api.Authentication;

public static class TenantScopeMiddleware
{
    /// <summary>
    /// Pose le commerce du jeton dans le contexte de la requête, d'où la base le lit pour son isolation.
    /// Sans jeton, rien n'est posé : la base n'expose alors aucune ligne (échec « fermé »).
    /// À placer juste après l'authentification.
    /// </summary>
    public static IApplicationBuilder UseTenantScope(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (HttpCurrentUser.Read(context.User) is { } identity)
            {
                context.RequestServices.GetRequiredService<ITenantScope>().Enter(identity.Tenant);
            }

            await next();
        });
}
