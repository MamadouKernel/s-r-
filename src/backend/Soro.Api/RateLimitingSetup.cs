using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Soro.Api.Authentication;

namespace Soro.Api;

public static class RateLimitingSetup
{
    private const int DefaultPermitsPerMinute = 600;

    /// <summary>
    /// Plafonne le débit par utilisateur (par adresse pour les requêtes non authentifiées).
    /// Valeur par défaut large : une caisse qui se resynchronise après une longue coupure doit passer.
    /// </summary>
    public static IServiceCollection AddSoroRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permits = configuration.GetValue("RateLimit:PermitsPerMinute", DefaultPermitsPerMinute);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permits,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });
        return services;
    }

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst(HttpCurrentUser.SubjectClaim)?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";
}
