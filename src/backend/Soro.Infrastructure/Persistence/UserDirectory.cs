using Microsoft.EntityFrameworkCore;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Infrastructure.Persistence;

/// <summary>Annuaire en lecture seule : le profil d'un utilisateur actif, utilisé pour valider une remise.</summary>
public sealed class UserDirectory(SoroDbContext db) : IUserDirectory
{
    public async Task<Role?> GetRoleAsync(UserId user, TenantId tenant, CancellationToken cancellationToken)
    {
        var roles = await db.Users
            .Where(row => row.Id == user.Value && row.TenantId == tenant.Value && row.Status == UserStatus.Active)
            .Select(row => (Role?)row.Role)
            .ToListAsync(cancellationToken);
        return roles.SingleOrDefault();
    }
}
