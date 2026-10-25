using Soro.Domain.Common;

namespace Soro.Domain.Stock;

/// <summary>Libellé d'un format dans sa famille (« 1,5 L », « 50 kg », « Pack de 6 ») et nom affiché qui en découle.</summary>
public static class FormatLabel
{
    public const int MaxLength = 40;
    public const int MaxDisplayNameLength = 200;

    /// <summary>Nettoie un libellé : espaces de bord retirés, espaces multiples réduits ; vide devient « sans libellé ».</summary>
    public static string? Normalize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var clean = string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Ensure.That(clean.Length <= MaxLength, $"Le format ne peut pas dépasser {MaxLength} caractères.");
        return clean;
    }

    /// <summary>Clé de comparaison : deux libellés qui ne diffèrent que par la casse ou les espaces sont le même libellé.</summary>
    public static string Key(string label) =>
        string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    public static string DisplayName(string familyName, string? label)
    {
        var name = label is null ? familyName : $"{familyName} · {label}";
        Ensure.That(name.Length <= MaxDisplayNameLength, $"Le nom affiché ne peut pas dépasser {MaxDisplayNameLength} caractères.");
        return name;
    }
}
