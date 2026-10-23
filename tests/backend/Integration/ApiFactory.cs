using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Soro.Application.Identity;
using Soro.Application.Signup;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Infrastructure.Security;

namespace Soro.Integration.Tests;

/// <summary>Le serveur d'API complet, en mémoire, branché sur le PostgreSQL de test et authentifié par JWT.</summary>
internal sealed class ApiFactory(
    PostgresFixture fixture,
    int? permitPerMinute = null,
    int? loginPermitsPerMinute = null,
    int? publicPermitsPerMinute = null) : WebApplicationFactory<Program>
{
    private const string Issuer = "https://soro.tests";
    private const string Audience = "soro-api-tests";

    private readonly SymmetricSecurityKey _signingKey = new(RandomNumberGenerator.GetBytes(32));
    private readonly byte[] _pepper = RandomNumberGenerator.GetBytes(32);

    /// <summary>Même hachage que le serveur : sert à créer des utilisateurs de test directement en base.</summary>
    public Pbkdf2PinHasher PinHasher => new(_pepper);

    /// <summary>Les SMS « envoyés » par le serveur : le seul endroit où un test lit un code de vérification.</summary>
    public CapturingSms Sms { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ISmsSender>(Sms);
        });

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Soro", fixture.AppConnectionString("api-tests"));
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(_signingKey.Key));
        builder.UseSetting("Security:PinPepper", Convert.ToBase64String(_pepper));
        if (loginPermitsPerMinute is { } loginPermits)
        {
            builder.UseSetting("RateLimit:LoginPermitsPerMinute", loginPermits.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (publicPermitsPerMinute is { } publicPermits)
        {
            builder.UseSetting("RateLimit:PublicPermitsPerMinute", publicPermits.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (permitPerMinute is { } permits)
        {
            builder.UseSetting("RateLimit:PermitsPerMinute", permits.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Client authentifié comme cet utilisateur du commerce donné.</summary>
    public HttpClient ClientFor(TenantId tenant, UserId user, Role role)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(tenant.Value.ToString(), user.Value.ToString(), role));
        return client;
    }

    public string Token(string? tenant, string? user, Role role)
    {
        var claims = new Dictionary<string, object> { ["role"] = role.ToString() };
        if (tenant is not null)
        {
            claims["tenant_id"] = tenant;
        }

        if (user is not null)
        {
            claims["sub"] = user;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            Claims = claims,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256),
        });
    }
}
