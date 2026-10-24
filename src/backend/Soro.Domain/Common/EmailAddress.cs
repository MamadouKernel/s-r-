namespace Soro.Domain.Common;

/// <summary>
/// Adresse e-mail, sous sa seule forme canonique (sans espaces autour, en minuscules) : deux écritures de la même adresse sont égales.
/// Identifiant de connexion du propriétaire et destinataire des codes de vérification (ADR 0005). La validation est volontairement simple
/// (une partie locale, un domaine avec au moins un point) : seule l'arrivée du code prouve que l'adresse existe.
/// </summary>
public readonly record struct EmailAddress
{
    private const int MaxLength = 254;
    private const int MaxLocalLength = 64;

    private EmailAddress(string value) => Value = value;

    /// <summary>Forme canonique, par exemple <c>mamadou@exemple.ci</c>.</summary>
    public string Value { get; }

    /// <summary>Partie avant le @.</summary>
    public string LocalPart => Value[..Value.IndexOf('@', StringComparison.Ordinal)];

    /// <summary>Adresse partiellement masquée pour les journaux : <c>m***@exemple.ci</c>.</summary>
    public string Masked => $"{Value[0]}***{Value[Value.IndexOf('@', StringComparison.Ordinal)..]}";

    public static EmailAddress Parse(string? input)
    {
        Ensure.That(
            TryNormalize(input, out var value),
            "L'adresse e-mail n'est pas valide : vérifiez-la, par exemple nom@exemple.ci.");
        return new EmailAddress(value);
    }

    public static bool TryParse(string? input, out EmailAddress address)
    {
        if (TryNormalize(input, out var value))
        {
            address = new EmailAddress(value);
            return true;
        }

        address = default;
        return false;
    }

    public override string ToString() => Value;

    private static bool TryNormalize(string? input, out string value)
    {
        value = string.Empty;
        var text = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Length is 0 or > MaxLength || text.Count(character => character == '@') != 1)
        {
            return false;
        }

        var at = text.IndexOf('@', StringComparison.Ordinal);
        var local = text[..at];
        var domain = text[(at + 1)..];
        if (local.Length is 0 or > MaxLocalLength || !IsValidLocal(local) || !IsValidDomain(domain))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool IsValidLocal(string local) =>
        local[0] != '.' && local[^1] != '.' && !local.Contains("..", StringComparison.Ordinal)
        && local.All(character => char.IsAsciiLetterOrDigit(character) || "._%+-".Contains(character, StringComparison.Ordinal));

    private static bool IsValidDomain(string domain)
    {
        var labels = domain.Split('.');
        return labels.Length >= 2
            && labels[^1].Length >= 2
            && labels[^1].All(char.IsAsciiLetter)
            && labels.All(label =>
                label.Length is >= 1 and <= 63
                && label[0] != '-' && label[^1] != '-'
                && label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
    }
}
