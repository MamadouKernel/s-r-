using Microsoft.IdentityModel.Tokens;

namespace Soro.Api.Authentication;

/// <summary>
/// Paramètres des jetons, lus une fois et partagés par la validation et l'émission. Le démarrage échoue si une
/// valeur manque ou si la clé de signature est trop courte : jamais de valeur par défaut pour un secret.
/// </summary>
public sealed record JwtSettings(string Issuer, string Audience, SymmetricSecurityKey Key, TimeSpan AccessTokenLifetime)
{
    private const int MinimumKeyBytes = 32;

    /// <summary>
    /// Durée d'un jeton : un service de caisse. Faute de décision sur l'accès local hors ligne (OP-6), 8 heures,
    /// réglable par <c>Jwt:AccessTokenMinutes</c>.
    /// </summary>
    private const int DefaultLifetimeMinutes = 480;

    public static JwtSettings From(IConfiguration configuration) =>
        new(
            Required(configuration, "Jwt:Issuer"),
            Required(configuration, "Jwt:Audience"),
            new SymmetricSecurityKey(ReadKey(Required(configuration, "Jwt:SigningKey"))),
            TimeSpan.FromMinutes(configuration.GetValue("Jwt:AccessTokenMinutes", DefaultLifetimeMinutes)));

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"La configuration {key} est obligatoire.");

    private static byte[] ReadKey(string base64)
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
