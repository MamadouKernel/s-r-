using Soro.Domain.Common;

namespace Soro.Application.Identity;

/// <summary>
/// Règle du mot de passe du propriétaire (ADR 0005). Sans second facteur à la connexion, le mot de passe est plus exigeant que le PIN :
/// 10 caractères au moins, une lettre et un chiffre, et jamais l'adresse e-mail.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    /// <summary>Message en français si le mot de passe est refusé, <c>null</c> s'il est accepté.</summary>
    public static string? Check(string? password, EmailAddress email)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "Choisissez un mot de passe.";
        }

        if (password.Length < MinLength)
        {
            return $"Le mot de passe doit faire au moins {MinLength} caractères.";
        }

        if (password.Length > MaxLength)
        {
            return $"Le mot de passe ne peut pas dépasser {MaxLength} caractères.";
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            return "Le mot de passe doit contenir au moins une lettre et un chiffre.";
        }

        var lowered = password.ToLowerInvariant();
        var local = email.LocalPart;
        if (lowered.Contains(email.Value, StringComparison.Ordinal) || (local.Length >= 4 && lowered.Contains(local, StringComparison.Ordinal)))
        {
            return "Le mot de passe ne doit pas contenir votre adresse e-mail.";
        }

        return null;
    }
}
