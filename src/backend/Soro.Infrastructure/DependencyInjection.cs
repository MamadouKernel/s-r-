using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Soro.Application.Cash;
using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Infrastructure.Persistence;

namespace Soro.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Enregistre la persistance PostgreSQL. Le contexte, les dépôts et l'intercepteur de commerce vivent le temps
    /// d'une requête ; l'intercepteur lit l'<c>ICurrentUser</c> de cette requête.
    /// </summary>
    public static IServiceCollection AddSoroPersistence(this IServiceCollection services, string connectionString)
    {
        // Le commerce de la requête : posé par l'authentification (jeton) ou par la connexion (avant le jeton).
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantAccessor>(provider => provider.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantScope>(provider => provider.GetRequiredService<TenantContext>());
        services.AddScoped<TenantSessionInterceptor>();
        services.AddDbContext<SoroDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .AddInterceptors(provider.GetRequiredService<TenantSessionInterceptor>())
            // Un fournisseur de services interne par requête serait un défaut de performance : échouer bruyamment.
            .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.ManyServiceProvidersCreatedWarning)));

        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        return services;
    }
}
