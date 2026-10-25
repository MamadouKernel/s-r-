using System.Globalization;
using Soro.Domain.Common;

namespace Soro.Domain.Stock;

/// <summary>
/// Codes-barres internes (docs/specs/etiquettes-tickets ETQ-01) : EAN-13 de la plage « usage en magasin » (préfixe 29, réservée par GS1 à la
/// circulation interne). Forme : <c>29</c> + 10 chiffres de séquence + clé de contrôle. Ils ne peuvent donc pas entrer en conflit avec le code
/// d'usine d'un produit du commerce.
/// </summary>
public static class InternalBarcode
{
    public const string Prefix = "29";
    public const long MaxSequence = 9_999_999_999;

    private const int Length = 13;

    /// <summary>Clé de contrôle EAN-13 des douze premiers chiffres (poids 1 et 3, de gauche à droite).</summary>
    public static int CheckDigit(string twelveDigits)
    {
        Ensure.That(twelveDigits.Length == Length - 1 && twelveDigits.All(char.IsAsciiDigit), "Douze chiffres sont nécessaires pour la clé de contrôle.");
        var sum = 0;
        for (var index = 0; index < twelveDigits.Length; index++)
        {
            sum += (twelveDigits[index] - '0') * (index % 2 == 0 ? 1 : 3);
        }

        return (10 - (sum % 10)) % 10;
    }

    public static bool IsValidEan13(string? code) =>
        code is { Length: Length } && code.All(char.IsAsciiDigit) && CheckDigit(code[..(Length - 1)]) == code[^1] - '0';

    public static string Compose(long sequence)
    {
        Ensure.That(sequence is >= 1 and <= MaxSequence, "La séquence des codes internes est épuisée.");
        var twelve = Prefix + sequence.ToString("D10", CultureInfo.InvariantCulture);
        return twelve + CheckDigit(twelve).ToString(CultureInfo.InvariantCulture);
    }

    public static bool IsInternal(string? code) => TryGetSequence(code, out _);

    /// <summary>Lit la séquence d'un code interne valide ; faux pour un code d'usine, une clé fausse ou une mauvaise longueur.</summary>
    public static bool TryGetSequence(string? code, out long sequence)
    {
        sequence = 0;
        return IsValidEan13(code)
            && code!.StartsWith(Prefix, StringComparison.Ordinal)
            && long.TryParse(code.AsSpan(Prefix.Length, 10), NumberStyles.None, CultureInfo.InvariantCulture, out sequence);
    }
}
