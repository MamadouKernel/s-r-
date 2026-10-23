using Soro.Application.Cash;
using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Reporting;
using Soro.Application.Sales;
using Soro.Application.Settings;
using Soro.Application.Signup;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Infrastructure.Security;

namespace Soro.Api;

public static class ApplicationRegistration
{
    private const string DefaultTermsVersion = "2026-10-brouillon";

    public static IServiceCollection AddSoroApplication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IShopSettings, DefaultShopSettings>();
        var pepper = ReadPepper(configuration);
        var hasher = new Pbkdf2PinHasher(pepper);
        services.AddSingleton<IPinHasher>(hasher);
        services.AddSingleton<IPasswordHasher>(hasher);
        services.AddSingleton<IVerificationCodes>(new VerificationCodes(pepper));
        services.AddSingleton<ISignupSettings>(new SignupSettings(
            configuration["Signup:TermsVersion"] ?? DefaultTermsVersion,
            configuration.GetValue("Signup:TrialDays", Domain.Tenancy.Commerce.DefaultTrialDays)));
        AddSmsSender(services, configuration, environment);

        services.AddScoped<OpenRegisterSessionHandler>();
        services.AddScoped<CloseRegisterSessionHandler>();
        services.AddScoped<RecordSaleHandler>();
        services.AddScoped<SyncSalesHandler>();
        services.AddScoped<CancelSaleHandler>();
        services.AddScoped<LogInHandler>();
        services.AddScoped<StartSignupHandler>();
        services.AddScoped<ResendSignupCodeHandler>();
        services.AddScoped<CompleteSignupHandler>();
        services.AddScoped<OwnerLogInHandler>();
        services.AddScoped<StartPasswordResetHandler>();
        services.AddScoped<ConfirmPasswordResetHandler>();
        services.AddScoped<RegisterUserHandler>();
        services.AddScoped<DisableUserHandler>();
        services.AddScoped<ListUsersHandler>();
        services.AddScoped<GetDirectoryHandler>();
        services.AddScoped<CreateProductHandler>();
        services.AddScoped<UpdateProductHandler>();
        services.AddScoped<DeactivateProductHandler>();
        services.AddScoped<ReceiveStockHandler>();
        services.AddScoped<RecordStockLossHandler>();
        services.AddScoped<ListProductsHandler>();
        services.AddScoped<ListLowStockHandler>();
        services.AddScoped<ListStockMovementsHandler>();
        services.AddScoped<ListSalesHandler>();
        services.AddScoped<GetSaleHandler>();
        services.AddScoped<ListSessionsHandler>();
        services.AddScoped<GetActivityReportHandler>();
        services.AddScoped<GetTopProductsHandler>();
        services.AddScoped<ListAuditLogHandler>();
        return services;
    }

    /// <summary>
    /// Le SMS réel dépend d'un fournisseur encore à choisir (ADR 0005). Hors développement et tests, le serveur refuse de démarrer
    /// tant qu'aucun fournisseur n'est configuré : il ne faut jamais « envoyer » des codes dans un journal en production.
    /// </summary>
    private static void AddSmsSender(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var provider = configuration["Sms:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "Sms:Provider est obligatoire hors développement : aucun fournisseur SMS n'est encore branché (ADR 0005).");
            }

            services.AddSingleton<ISmsSender, LoggingSmsSender>();
            return;
        }

        throw new InvalidOperationException($"Le fournisseur SMS « {provider} » n'est pas pris en charge.");
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
