using Microsoft.Extensions.Caching.Memory;
using Soro.Application.Common;
using Soro.Application.Subscription;
using Soro.Domain.Common;
using Soro.Domain.Subscription;

namespace Soro.Api.Authentication;

public static class SubscriptionAccessMiddleware
{
    /// <summary>L'état d'un commerce change rarement : on le garde quelques secondes plutôt que de relire la base à chaque requête (<c>Subscription:StateCacheSeconds</c>, 0 pour ne rien garder).</summary>
    private const int DefaultCacheSeconds = 20;

    private const string Prefix = "/api/v1/";

    /// <summary>
    /// Lecture seule (RG-14, docs/specs/abonnement ABO-07) : une fois le délai de grâce écoulé, toute écriture en ligne est refusée avec
    /// <c>subscription_expired</c>, sauf se renseigner sur son abonnement et déclarer un paiement. Les envois de la file hors ligne (ventes,
    /// ouvertures et clôtures de caisse) passent pendant sept jours encore : ces ventes ont eu lieu, les rejeter ne rendrait pas la marchandise ; elles sont
    /// signalées au journal. Passé ce délai, il faut payer : la file est conservée sur la caisse et part dès le paiement validé. Un commerce inconnu n'est pas bloqué. À placer après <see cref="TenantScopeMiddleware"/> et l'accès appareil.
    /// </summary>
    public static IApplicationBuilder UseSubscriptionAccess(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (IsWrite(context.Request.Method)
                && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith(Prefix + "subscription", StringComparison.OrdinalIgnoreCase)
                && HttpCurrentUser.Read(context.User) is { } identity)
            {
                var state = await StateOfAsync(context, identity.Tenant);
                if (state is { IsReadOnly: true })
                {
                    if (IsOfflineQueueEndpoint(context.Request.Method, path)
                        && context.RequestServices.GetRequiredService<IClock>().UtcNow < state.ReadOnlyFrom.AddDays(SubscriptionRules.OfflineQueueDays))
                    {
                        await context.RequestServices.GetRequiredService<RecordExpiredSyncHandler>().HandleAsync(path, context.RequestAborted);
                        await next();
                        return;
                    }

                    await ResultHttpMappingProblem.Write(
                        context,
                        StatusCodes.Status403Forbidden,
                        ErrorCodes.SubscriptionExpired,
                        "Votre abonnement est terminé : la boutique est en lecture seule. Vos données sont conservées. Payez votre abonnement pour reprendre.",
                        "Abonnement terminé");
                    return;
                }
            }

            await next();
        });

    private static bool IsWrite(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

    /// <summary>Les envois de la file de la caisse : ventes synchronisées, ouverture et clôture de caisse.</summary>
    private static bool IsOfflineQueueEndpoint(string method, string path) =>
        HttpMethods.IsPost(method)
        && (string.Equals(path, Prefix + "sales/sync", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, Prefix + "register-sessions", StringComparison.OrdinalIgnoreCase)
            || (path.StartsWith(Prefix + "register-sessions/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/close", StringComparison.OrdinalIgnoreCase)));

    private static async Task<SubscriptionState?> StateOfAsync(HttpContext context, TenantId tenant)
    {
        var seconds = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Subscription:StateCacheSeconds", DefaultCacheSeconds);
        var handler = context.RequestServices.GetRequiredService<GetSubscriptionStateHandler>();
        if (seconds <= 0)
        {
            return await handler.HandleAsync(tenant, context.RequestAborted);
        }

        var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
        var key = $"subscription:{tenant.Value:N}";
        if (cache.TryGetValue(key, out SubscriptionState? known))
        {
            return known;
        }

        var state = await handler.HandleAsync(tenant, context.RequestAborted);
        cache.Set(key, state, TimeSpan.FromSeconds(seconds));
        return state;
    }
}
