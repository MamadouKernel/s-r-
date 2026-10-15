using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Soro.Application.Identity;

namespace Soro.Api.Authentication;

public static class AuthenticationSetup
{
    private const int MinimumKeyBytes = 32;

    /// <summary>
    /// Valide les jetons JWT (signature HMAC-SHA256, émetteur, public, expiration). L'émission des jetons
    /// (connexion par PIN, mot de passe + SMS) relève du module Identité, pas de cette API.
    /// Démarrage refusé si la configuration est absente ou trop faible.
    /// </summary>
    public static IServiceCollection AddSoroAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var issuer = Required(configuration, "Jwt:Issuer");
        var audience = Required(configuration, "Jwt:Audience");
        var signingKey = new SymmetricSecurityKey(ReadSigningKey(Required(configuration, "Jwt:SigningKey")));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = signingKey,
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

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"La configuration {key} est obligatoire.");

    private static byte[] ReadSigningKey(string base64)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Jwt:SigningKey doit être encodée en Base64.", exception);
        }

        return key.Length >= MinimumKeyBytes
            ? key
            : throw new InvalidOperationException($"Jwt:SigningKey doit contenir au moins {MinimumKeyBytes} octets.");
    }
}
