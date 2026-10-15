using Microsoft.EntityFrameworkCore;
using Soro.Application.Identity;

namespace Soro.Infrastructure.Persistence;

public static class PersistenceOptions
{
    /// <summary>
    /// Sans utilisateur : connexion d'administration (migrations). Avec utilisateur : le commerce courant
    /// est posé sur chaque connexion et l'isolation RLS s'applique.
    /// </summary>
    public static DbContextOptions<SoroDbContext> Create(string connectionString, ICurrentUser? currentUser = null)
    {
        var builder = new DbContextOptionsBuilder<SoroDbContext>().UseNpgsql(connectionString);
        if (currentUser is not null)
        {
            builder.AddInterceptors(new TenantSessionInterceptor(currentUser));
        }

        return builder.Options;
    }
}
