using Soro.Application.Cash;
using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Application.Settings;
using Soro.Domain.Common;

namespace Soro.Api;

public static class ApplicationRegistration
{
    public static IServiceCollection AddSoroApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IShopSettings, DefaultShopSettings>();
        services.AddSingleton<IUserDirectory, UnavailableUserDirectory>();

        services.AddScoped<OpenRegisterSessionHandler>();
        services.AddScoped<CloseRegisterSessionHandler>();
        services.AddScoped<RecordSaleHandler>();
        services.AddScoped<SyncSalesHandler>();
        services.AddScoped<CancelSaleHandler>();
        return services;
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Seuils par défaut tant que les paramètres du commerce ne sont pas stockés (module Paramètres).</summary>
public sealed class DefaultShopSettings : IShopSettings
{
    public Task<ShopSettings> GetAsync(TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(ShopSettings.Default);
}

/// <summary>
/// Provisoire : l'annuaire des utilisateurs n'existe pas encore (module Identité). Aucun autre utilisateur n'est
/// reconnu comme gérant, donc une remise au-delà du seuil n'est acceptée que d'un gérant connecté lui-même.
/// </summary>
public sealed class UnavailableUserDirectory : IUserDirectory
{
    public Task<Role?> GetRoleAsync(UserId user, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult<Role?>(null);
}
