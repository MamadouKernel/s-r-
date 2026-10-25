using Soro.Application.Cash;
using Soro.Application.Common;
using Soro.Application.Customers;
using Soro.Application.Devices;
using Soro.Application.Identity;
using Soro.Application.Reporting;
using Soro.Application.Sales;
using Soro.Application.Settings;
using Soro.Application.Signup;
using Soro.Application.Stock;
using Soro.Application.Subscription;
using MailKit.Security;
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
        services.AddSingleton<IDeviceSecrets>(new DeviceSecrets(pepper));
        services.AddSingleton<IDevicePolicy>(new DevicePolicy(configuration.GetValue("Devices:Required", true)));
        services.AddSingleton<ISignupSettings>(new SignupSettings(
            configuration["Signup:TermsVersion"] ?? DefaultTermsVersion,
            configuration.GetValue("Signup:TrialDays", Domain.Tenancy.Commerce.DefaultTrialDays)));
        AddEmailSender(services, configuration, environment);
        services.AddSingleton<ISubscriptionSettings>(Soro.Infrastructure.Subscription.SubscriptionSettings.From(
            key => configuration[key], allowDevelopmentDefaults: environment.IsDevelopment() || environment.IsEnvironment("Testing")));
        services.AddMemoryCache();
        services.AddScoped<Soro.Application.Export.ExportDataHandler>();
        services.AddScoped<GetSubscriptionHandler>();
        services.AddScoped<DeclareSubscriptionPaymentHandler>();
        services.AddScoped<GetSubscriptionStateHandler>();
        services.AddScoped<RecordExpiredSyncHandler>();

        services.AddScoped<OpenRegisterSessionHandler>();
        services.AddScoped<CloseRegisterSessionHandler>();
        services.AddScoped<RecordSaleHandler>();
        services.AddScoped<SyncSalesHandler>();
        services.AddScoped<CancelSaleHandler>();
        services.AddScoped<LogInHandler>();
        services.AddScoped<CreateCustomerHandler>();
        services.AddScoped<UpdateCustomerHandler>();
        services.AddScoped<DeactivateCustomerHandler>();
        services.AddScoped<ListCustomersHandler>();
        services.AddScoped<RecordRepaymentHandler>();
        services.AddScoped<ListRepaymentsHandler>();
        services.AddScoped<CreateDeviceInvitationHandler>();
        services.AddScoped<EnrollDeviceHandler>();
        services.AddScoped<ListDevicesHandler>();
        services.AddScoped<RevokeDeviceHandler>();
        services.AddScoped<CheckDeviceAccessHandler>();
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
        services.AddScoped<CreateFamilyHandler>();
        services.AddScoped<AddFormatHandler>();
        services.AddScoped<UpdateFamilyHandler>();
        services.AddScoped<GroupIntoFamilyHandler>();
        services.AddScoped<SetPhotoHandler>();
        services.AddScoped<RemovePhotoHandler>();
        services.AddScoped<GetPhotoHandler>();
        services.AddScoped<GetImportTemplateHandler>();
        services.AddScoped<PreviewProductImportHandler>();
        services.AddScoped<ImportProductsHandler>();
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
    /// L'envoi réel passe par SMTP (MailKit). Hors développement et tests, le serveur refuse de démarrer sans réglages
    /// <c>Mail:Smtp</c> : il ne faut jamais « envoyer » des codes dans un journal en production.
    /// </summary>
    private static void AddEmailSender(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var smtp = configuration.GetSection("Mail:Smtp");
        if (string.IsNullOrWhiteSpace(smtp["Host"]))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "Mail:Smtp:Host est obligatoire hors développement : sans serveur d'envoi, aucun code de vérification ne peut partir.");
            }

            services.AddSingleton<IEmailSender, LoggingEmailSender>();
            return;
        }

        var from = smtp["FromAddress"];
        if (!EmailAddress.TryParse(from, out _))
        {
            throw new InvalidOperationException("Mail:Smtp:FromAddress doit être une adresse e-mail valide (l'expéditeur des codes).");
        }

        var security = Enum.TryParse<SecureSocketOptions>(smtp["Security"], ignoreCase: true, out var parsed)
            ? parsed
            : SecureSocketOptions.StartTls;
        services.AddSingleton(new SmtpSettings(
            smtp["Host"]!,
            smtp.GetValue("Port", 587),
            smtp["UserName"],
            smtp["Password"],
            from!,
            smtp["FromName"] ?? "Sôrô",
            security));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
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

/// <summary>Réglage <c>Devices:Required</c> : vrai par défaut (ADR 0006).</summary>
public sealed record DevicePolicy(bool Required) : IDevicePolicy;

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
