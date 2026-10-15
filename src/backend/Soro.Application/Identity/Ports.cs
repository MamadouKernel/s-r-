using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Identity;

/// <summary>
/// Utilisateur authentifié. Le tenant vient toujours d'ici, jamais d'une valeur envoyée par le client (RG-15).
/// </summary>
public interface ICurrentUser
{
    TenantId Tenant { get; }

    UserId User { get; }

    Role Role { get; }
}

public interface IUserDirectory
{
    Task<Role?> GetRoleAsync(UserId user, TenantId tenant, CancellationToken cancellationToken);
}
