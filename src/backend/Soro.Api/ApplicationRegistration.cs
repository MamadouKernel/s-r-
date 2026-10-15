using Soro.Application.Cash;
using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Application.Settings;
using Soro.Domain.Common;
using Soro.Infrastructure.Security;

namespace Soro.Api;

public static class ApplicationRegistration
{
    public static IServiceCollection AddSoroApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IShopSettings, DefaultShopSettings>();
        services.AddSingleton<IPinHasher>(new Pbkdf2PinHasher(ReadPepper(configuration)));

        services.AddScoped<OpenRegisterSessionHandler>();
        services.AddScoped<CloseRegisterSessionHandler>();
        services.AddScoped<RecordSaleHandler>();
        services.AddScoped<SyncSalesHandler>();
        services.AddScoped<CancelSaleHandler>();
        services.AddScoped<LogInHandler>();
        services.AddScoped<RegisterUserHandler>();
        services.AddScoped<DisableUserHandler>();
        return services;
    }

    /// <summary>Le poivre des PIN est un secret du serveur (jamais en base) : démarrage refusé s'il manque.</summary>
    private static byte[] ReadPepper(IConfiguration configuration)
    {
        const string key = "Security:PinPepper";
        var value = configuration[key];
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"La configuration {key} est obligatoire.");
        }

        try
        {
            var pepper = Convert.FromBase64String(value);
            return pepper.Length >= Pbkdf2PinHasher.MinimumPepperBytes
                ? pepper
                : throw new InvalidOperationException(
                    $"{key} doit contenir au moins {Pbkdf2PinHasher.MinimumPepperBytes} octets.");
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{key} doit être encodée en Base64.", exception);
        }
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Seuils par défaut tant que les paramètres du commerce ne sont pas stockés (tâche T028).</summary>
public sealed class DefaultShopSettings : IShopSettings
{
    public Task<ShopSettings> GetAsync(TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(ShopSettings.Default);
}
