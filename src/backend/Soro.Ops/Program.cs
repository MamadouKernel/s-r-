using System.Globalization;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Soro.Application.Common;
using Soro.Application.Signup;
using Soro.Application.Subscription;
using Soro.Domain.Common;
using Soro.Domain.Subscription;
using Soro.Infrastructure;
using Soro.Infrastructure.Security;
using Soro.Infrastructure.Subscription;

// Outil d'exploitation de l'équipe Sôrô (docs/decisions/0011) : valider les paiements d'abonnement et envoyer les rappels.
// Il se connecte à la base avec un rôle qui voit tous les commerces (SORO_OPS_CONNECTION) : l'API des commerces n'en a jamais le droit.

if (args.Length == 0 || args[0] is "aide" or "--help" or "-h")
{
    Console.WriteLine(
        """
        Sôrô — outil d'exploitation

          soro-ops paiements                   liste les paiements d'abonnement en attente
          soro-ops valider <identifiant>       valide un paiement (la boutique est prolongée, un e-mail confirme)
          soro-ops refuser <identifiant> <motif>   refuse un paiement avec un motif (un e-mail le dit au propriétaire)
          soro-ops rappels                     envoie les rappels d'échéance (à lancer chaque jour)

        Variables : SORO_OPS_CONNECTION (obligatoire), Mail__Smtp__* (envoi des e-mails),
                    Subscription__MonthlyPriceFcfa, Subscription__AnnualPriceFcfa, Subscription__PayNumbers__Wave…
        """);
    return args.Length == 0 ? 1 : 0;
}

var connection = Environment.GetEnvironmentVariable("SORO_OPS_CONNECTION");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("La variable SORO_OPS_CONNECTION est obligatoire (chaîne de connexion avec un rôle qui voit tous les commerces).");
    return 1;
}

var services = new ServiceCollection();
services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true));
services.AddSoroPersistence(connection);
services.AddSingleton<IClock, OpsClock>();
services.AddSingleton<ISubscriptionSettings>(SubscriptionSettings.From(Setting, allowDevelopmentDefaults: false));
AddEmail(services);
services.AddScoped<ListPendingSubscriptionPaymentsHandler>();
services.AddScoped<ValidateSubscriptionPaymentHandler>();
services.AddScoped<RefuseSubscriptionPaymentHandler>();
services.AddScoped<SendSubscriptionRemindersHandler>();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var scoped = scope.ServiceProvider;

try
{
    switch (args[0])
    {
        case "paiements":
            var pending = await scoped.GetRequiredService<ListPendingSubscriptionPaymentsHandler>().HandleAsync();
            if (pending.Count == 0)
            {
                Console.WriteLine("Aucun paiement en attente.");
            }

            foreach (var item in pending)
            {
                Console.WriteLine(
                    $"{item.Id}  {item.DeclaredAt:yyyy-MM-dd HH:mm}  {item.CommerceName} <{item.OwnerEmail}>  {item.Period}  {item.Method}  réf. {item.Reference}  {Money.Fcfa(item.AmountFcfa)}");
            }

            return 0;

        case "valider" when args.Length == 2 && Guid.TryParse(args[1], out var validateId):
            var validated = await scoped.GetRequiredService<ValidateSubscriptionPaymentHandler>().HandleAsync(new SubscriptionPaymentId(validateId));
            if (!validated.IsSuccess)
            {
                Console.Error.WriteLine(validated.Error!.Message);
                return 1;
            }

            Console.WriteLine(
                $"Paiement validé : abonnement actif jusqu'au {validated.Value!.PaidUntil.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}."
                + (validated.Value.EmailSent ? " E-mail de confirmation envoyé." : " L'e-mail de confirmation n'a pas pu partir : prévenez le propriétaire."));
            return 0;

        case "refuser" when args.Length >= 3 && Guid.TryParse(args[1], out var refuseId):
            var refused = await scoped.GetRequiredService<RefuseSubscriptionPaymentHandler>()
                .HandleAsync(new RefuseSubscriptionPaymentCommand(new SubscriptionPaymentId(refuseId), string.Join(' ', args.Skip(2))));
            if (!refused.IsSuccess)
            {
                Console.Error.WriteLine(refused.Error!.Message);
                return 1;
            }

            Console.WriteLine("Paiement refusé. Le propriétaire a été prévenu par e-mail.");
            return 0;

        case "rappels":
            var sent = await scoped.GetRequiredService<SendSubscriptionRemindersHandler>().HandleAsync();
            Console.WriteLine($"{sent.Value} rappel(s) envoyé(s).");
            return 0;

        default:
            Console.Error.WriteLine("Commande inconnue ou incomplète. Lancez « soro-ops aide ».");
            return 1;
    }
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static string? Setting(string key) => Environment.GetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal));

static void AddEmail(IServiceCollection services)
{
    var host = Setting("Mail:Smtp:Host");
    if (string.IsNullOrWhiteSpace(host))
    {
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        return;
    }

    var from = Setting("Mail:Smtp:FromAddress");
    if (!EmailAddress.TryParse(from, out _))
    {
        throw new InvalidOperationException("Mail__Smtp__FromAddress doit être une adresse e-mail valide.");
    }

    var security = Enum.TryParse<SecureSocketOptions>(Setting("Mail:Smtp:Security"), ignoreCase: true, out var parsed) ? parsed : SecureSocketOptions.StartTls;
    services.AddSingleton(new SmtpSettings(
        host,
        int.TryParse(Setting("Mail:Smtp:Port"), out var port) ? port : 587,
        Setting("Mail:Smtp:UserName"),
        Setting("Mail:Smtp:Password"),
        from!,
        Setting("Mail:Smtp:FromName") ?? "Sôrô",
        security));
    services.AddSingleton<IEmailSender, SmtpEmailSender>();
}

internal sealed class OpsClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
