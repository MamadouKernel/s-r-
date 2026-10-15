namespace Soro.Application.Identity;

public static class PinPolicy
{
    public const int MinLength = 4;
    public const int MaxLength = 6;

    /// <summary>Un code PIN comporte 4 à 6 chiffres (saisie rapide à la caisse).</summary>
    public static bool IsValid(string? pin) =>
        pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);
}
