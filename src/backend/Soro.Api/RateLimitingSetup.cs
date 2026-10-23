using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Soro.Api.Authentication;

namespace Soro.Api;

public static class RateLimitingSetup
{
    public const string LoginPolicy = "login";
    public const string PublicPolicy = "public";

    private const int DefaultPermitsPerMinute = 600;
    private const int DefaultLoginPermitsPerMinute = 10;
    private const int DefaultPublicPermitsPerMinute = 10;

    /// <summary>
    /// Deux plafonds. Global : par utilisateur (par adresse pour les non authentifiés), large pour qu'une caisse qui
    /// se resynchronise après une longue coupure passe. Connexion : strict, par adresse, contre les essais de PIN.
    /// </summary>
    public static IServiceCollection AddSoroRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permits = configuration.GetValue("RateLimit:PermitsPerMinute", DefaultPermitsPerMinute);
        var loginPermits = configuration.GetValue("RateLimit:LoginPermitsPerMinute", DefaultLoginPermitsPerMinute);
        var publicPermits = configuration.GetValue("RateLimit:PublicPermitsPerMinute", DefaultPublicPermitsPerMinute);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => PerMinute(permits)));
            options.AddPolicy(LoginPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(Address(context), _ => PerMinute(loginPermits)));
            // Inscription et mot de passe oublié : chaque appel peut coûter un SMS, donc un plafond par adresse.
            options.AddPolicy(PublicPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(Address(context), _ => PerMinute(publicPermits)));
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions PerMinute(int permits) =>
        new() { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst(HttpCurrentUser.SubjectClaim)?.Value ?? Address(context);

    private static string Address(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
}
