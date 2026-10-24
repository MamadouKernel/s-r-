using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Api.Authentication;

/// <summary>Émet le jeton d'accès : les trois revendications lues ensuite à chaque requête, et rien d'autre.</summary>
public sealed class JwtTokenIssuer(JwtSettings settings) : ITokenIssuer
{
    public AccessToken Issue(TenantId tenant, UserId user, Role role, DateTimeOffset now, DeviceId? device = null)
    {
        var expiresAt = now + settings.AccessTokenLifetime;
        var claims = new Dictionary<string, object>
        {
            [HttpCurrentUser.SubjectClaim] = user.Value.ToString(),
            [HttpCurrentUser.TenantClaim] = tenant.Value.ToString(),
            [HttpCurrentUser.RoleClaim] = role.ToString(),
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
        if (device is { } deviceId)
        {
            claims[HttpCurrentUser.DeviceClaim] = deviceId.Value.ToString();
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(settings.Key, SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expiresAt);
    }
}
