using Microsoft.EntityFrameworkCore;
using Soro.Application.Identity;

namespace Soro.Infrastructure.Persistence;

public static class PersistenceOptions
{
    /// <summary>Connexion d'administration (migrations) : aucun commerce posé.</summary>
    public static DbContextOptions<SoroDbContext> Create(string connectionString) =>
        Build(connectionString, tenant: null);

    /// <summary>Le commerce de l'utilisateur est posé sur chaque connexion : l'isolation RLS s'applique.</summary>
    public static DbContextOptions<SoroDbContext> Create(string connectionString, ICurrentUser currentUser) =>
        Build(connectionString, new CurrentUserTenantAccessor(currentUser));

    /// <summary>Le commerce est lu à chaque ouverture de connexion, quand il est connu.</summary>
    public static DbContextOptions<SoroDbContext> Create(string connectionString, ITenantAccessor tenant) =>
        Build(connectionString, tenant);

    private static DbContextOptions<SoroDbContext> Build(string connectionString, ITenantAccessor? tenant)
    {
        var builder = new DbContextOptionsBuilder<SoroDbContext>().UseNpgsql(connectionString);
        if (tenant is not null)
        {
            builder.AddInterceptors(new TenantSessionInterceptor(tenant));
        }

        return builder.Options;
    }
}
