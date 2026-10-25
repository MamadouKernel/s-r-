using Microsoft.Extensions.Caching.Memory;
using Soro.Application.Common;
using Soro.Application.Identity;

namespace Soro.Api.Authentication;

public static class SessionAccessMiddleware
{
    /// <summary>Un changement d'accès (nouveau PIN, désactivation, nouveau profil) se voit au plus tard après ce délai (<c>Sessions:StateCacheSeconds</c>, 0 pour ne rien garder).</summary>
    private const int DefaultCacheSeconds = 15;

    /// <summary>
    /// Met fin aux sessions périmées (docs/specs/equipe-gestion-complete EQP-05) : un jeton émis avant le dernier changement de PIN, de profil ou
    /// d'état de son titulaire, ou celui d'un membre désactivé, est refusé avec <c>session_outdated</c>, sans attendre son expiration. Le propriétaire,
    /// qui n'a pas de PIN, n'est touché que si son compte disparaît. À placer après <see cref="TenantScopeMiddleware"/> : la lecture se fait sous
    /// l'isolation du commerce.
    /// </summary>
    public static IApplicationBuilder UseSessionAccess(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (HttpCurrentUser.Read(context.User) is { } identity && !await IsCurrentAsync(context, identity))
            {
                await ResultHttpMappingProblem.Write(
                    context,
                    StatusCodes.Status401Unauthorized,
                    ErrorCodes.SessionOutdated,
                    "Votre accès a changé : reconnectez-vous.",
                    "Session terminée");
                return;
            }

            await next();
        });

    private static async Task<bool> IsCurrentAsync(HttpContext context, CurrentIdentity identity)
    {
        var issuedAt = IssuedAt(context);
        var seconds = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Sessions:StateCacheSeconds", DefaultCacheSeconds);
        var handler = context.RequestServices.GetRequiredService<CheckSessionHandler>();
        if (seconds <= 0)
        {
            return await handler.HandleAsync(identity.Tenant, identity.User, issuedAt, context.RequestAborted);
        }

        var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
        var key = $"session:{identity.User.Value:N}:{issuedAt.ToUnixTimeSeconds()}";
        if (cache.TryGetValue(key, out bool known))
        {
            return known;
        }

        var current = await handler.HandleAsync(identity.Tenant, identity.User, issuedAt, context.RequestAborted);
        cache.Set(key, current, TimeSpan.FromSeconds(seconds));
        return current;
    }

    /// <summary>Instant d'émission du jeton (<c>iat</c>, en secondes). Un jeton qui n'en porte pas est traité comme le plus ancien possible.</summary>
    private static DateTimeOffset IssuedAt(HttpContext context) =>
        long.TryParse(context.User.FindFirst("iat")?.Value, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UnixEpoch;
}
