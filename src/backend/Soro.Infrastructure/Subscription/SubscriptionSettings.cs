using Soro.Application.Subscription;
using Soro.Domain.Subscription;

namespace Soro.Infrastructure.Subscription;

/// <summary>Prix et numéros de paiement de Sôrô, lus de la configuration (docs/decisions/0011). Aucun montant n'est écrit dans le code.</summary>
public sealed class SubscriptionSettings : ISubscriptionSettings
{
    private SubscriptionSettings(long monthly, long annual, IReadOnlyDictionary<SubscriptionMethod, string> numbers)
    {
        MonthlyPriceFcfa = monthly;
        AnnualPriceFcfa = annual;
        PayNumbers = numbers;
    }

    public long MonthlyPriceFcfa { get; }

    public long AnnualPriceFcfa { get; }

    public IReadOnlyDictionary<SubscriptionMethod, string> PayNumbers { get; }

    /// <summary>
    /// Lit <c>Subscription:MonthlyPriceFcfa</c>, <c>Subscription:AnnualPriceFcfa</c> et <c>Subscription:PayNumbers:{Wave|OrangeMoney|MtnMomo|MoovMoney}</c>.
    /// Sans valeur, le développement prend des valeurs d'essai ; ailleurs le démarrage est refusé : un prix oublié ne doit pas partir en production.
    /// </summary>
    public static SubscriptionSettings From(Func<string, string?> read, bool allowDevelopmentDefaults)
    {
        var monthly = ReadAmount(read, "Subscription:MonthlyPriceFcfa", allowDevelopmentDefaults ? 5_000 : null);
        var annual = ReadAmount(read, "Subscription:AnnualPriceFcfa", allowDevelopmentDefaults ? 50_000 : null);
        var numbers = new Dictionary<SubscriptionMethod, string>();
        foreach (var method in Enum.GetValues<SubscriptionMethod>())
        {
            if (read($"Subscription:PayNumbers:{method}") is { } number && !string.IsNullOrWhiteSpace(number))
            {
                numbers[method] = number.Trim();
            }
        }

        if (numbers.Count == 0)
        {
            if (!allowDevelopmentDefaults)
            {
                throw new InvalidOperationException(
                    "Subscription:PayNumbers:Wave (ou OrangeMoney, MtnMomo, MoovMoney) est obligatoire : sans numéro, aucun client ne sait où payer.");
            }

            numbers[SubscriptionMethod.Wave] = "00 00 00 00 00";
        }

        return new SubscriptionSettings(monthly, annual, numbers);
    }

    private static long ReadAmount(Func<string, string?> read, string key, long? developmentDefault)
    {
        var text = read(key);
        if (string.IsNullOrWhiteSpace(text))
        {
            return developmentDefault ?? throw new InvalidOperationException($"{key} est obligatoire (prix en FCFA, nombre entier).");
        }

        return long.TryParse(text.Trim(), out var value) && value > 0
            ? value
            : throw new InvalidOperationException($"{key} doit être un nombre entier de FCFA strictement positif.");
    }
}
