namespace Soro.Domain.Common;

/// <summary>
/// Numéro de téléphone ivoirien (+225, 10 chiffres), sous sa seule forme canonique <c>+225XXXXXXXXXX</c> : deux écritures
/// du même numéro sont égales. Les autres pays sont hors lot (docs/specs/inscription/spec.md).
/// </summary>
public readonly record struct PhoneNumber
{
    private const string CountryCode = "225";
    private const int LocalLength = 10;

    private PhoneNumber(string value) => Value = value;

    /// <summary>Forme canonique, par exemple <c>+2250707070707</c>.</summary>
    public string Value { get; }

    /// <summary>Numéro partiellement masqué pour les messages et les journaux : <c>+225 07 ** ** ** 07</c>.</summary>
    public string Masked
    {
        get
        {
            var local = Value[^LocalLength..];
            return $"+{CountryCode} {local[..2]} ** ** ** {local[^2..]}";
        }
    }

    public static PhoneNumber Parse(string? input)
    {
        Ensure.That(
            TryNormalize(input, out var value),
            "Le numéro de téléphone n'est pas valide : saisissez un numéro ivoirien à 10 chiffres.");
        return new PhoneNumber(value);
    }

    public static bool TryParse(string? input, out PhoneNumber number)
    {
        if (TryNormalize(input, out var value))
        {
            number = new PhoneNumber(value);
            return true;
        }

        number = default;
        return false;
    }

    public override string ToString() => Value;

    private static bool TryNormalize(string? input, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = new string(input.Where(character => character is not (' ' or '.' or '-' or '(' or ')' or ' ' or ' ')).ToArray());
        string digits;
        if (text.StartsWith('+'))
        {
            digits = text[1..];
        }
        else if (text.StartsWith("00", StringComparison.Ordinal))
        {
            digits = text[2..];
        }
        else
        {
            digits = text;
        }

        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        var international = text.StartsWith('+') || text.StartsWith("00", StringComparison.Ordinal);
        if (digits.Length == LocalLength && !international)
        {
            value = $"+{CountryCode}{digits}";
            return true;
        }

        if (digits.Length == CountryCode.Length + LocalLength && digits.StartsWith(CountryCode, StringComparison.Ordinal))
        {
            value = $"+{digits}";
            return true;
        }

        return false;
    }
}
