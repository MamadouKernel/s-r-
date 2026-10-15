using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Soro.Application.Identity;

namespace Soro.Api.Authentication;

public static class AuthenticationSetup
{
    /// <summary>
    /// Valide les jetons JWT (signature HMAC-SHA256, émetteur, public, expiration) et les émet à la connexion.
    /// Démarrage refusé si la configuration est absente ou trop faible.
    /// </summary>
    public static IServiceCollection AddSoroAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = JwtSettings.From(configuration);
        services.AddSingleton(settings);
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = settings.Key,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
                options.Events = new JwtBearerEvents
                {
                    // Un jeton valide mais sans commerce, utilisateur ou profil exploitable n'ouvre aucun accès.
                    OnTokenValidated = context =>
                    {
                        if (HttpCurrentUser.Read(context.Principal) is null)
                        {
                            context.Fail("Le jeton ne porte pas d'identité exploitable.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return services;
    }
}
