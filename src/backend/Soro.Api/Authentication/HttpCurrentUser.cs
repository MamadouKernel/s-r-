using System.Security.Claims;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Api.Authentication;

/// <summary>Identité lue dans le jeton : commerce, utilisateur et profil. Jamais dans le corps de la requête (RG-15).</summary>
public readonly record struct CurrentIdentity(TenantId Tenant, UserId User, Role Role);

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string TenantClaim = "tenant_id";
    public const string SubjectClaim = "sub";
    public const string RoleClaim = "role";
    public const string DeviceClaim = "device_id";

    public TenantId Tenant => Identity.Tenant;

    public UserId User => Identity.User;

    public Role Role => Identity.Role;

    private CurrentIdentity Identity =>
        Read(accessor.HttpContext?.User)
        ?? throw new InvalidOperationException("Aucun utilisateur authentifié sur cette requête.");

    /// <summary>Renvoie l'identité si le jeton porte un commerce, un utilisateur et un profil valides.</summary>
    public static CurrentIdentity? Read(ClaimsPrincipal? principal)
    {
        if (principal is null
            || !TryGuid(principal.FindFirstValue(TenantClaim), out var tenant)
            || !TryGuid(principal.FindFirstValue(SubjectClaim), out var user)
            || !TryRole(principal.FindFirstValue(RoleClaim), out var role))
        {
            return null;
        }

        return new CurrentIdentity(new TenantId(tenant), new UserId(user), role);
    }

    /// <summary>L'appareil d'où vient le jeton, s'il en porte un.</summary>
    public static DeviceId? ReadDevice(ClaimsPrincipal? principal) =>
        principal is not null && TryGuid(principal.FindFirstValue(DeviceClaim), out var device) ? new DeviceId(device) : null;

    private static bool TryGuid(string? value, out Guid result) =>
        Guid.TryParse(value, out result) && result != Guid.Empty;

    // Nom exact seulement : "1" ou "manager" ne sont pas des profils valides.
    private static bool TryRole(string? value, out Role role) =>
        Enum.TryParse(value, ignoreCase: false, out role) && Enum.GetName(role) == value;
}
