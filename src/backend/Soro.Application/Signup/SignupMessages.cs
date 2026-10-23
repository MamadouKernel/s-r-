using Soro.Domain.Tenancy;

namespace Soro.Application.Signup;

/// <summary>Textes des SMS. Le code n'apparaît que dans le SMS, jamais dans une réponse de l'API ni dans un journal en production.</summary>
internal static class SignupMessages
{
    private static string Lifetime => $"{(int)PhoneVerification.CodeLifetime.TotalMinutes} minutes";

    public static string Verification(string code) =>
        $"Sôrô : votre code de vérification est {code}. Il expire dans {Lifetime}. Ne le communiquez à personne.";

    public static string PasswordReset(string code) =>
        $"Sôrô : votre code pour réinitialiser votre mot de passe est {code}. Il expire dans {Lifetime}. Ne le communiquez à personne.";
}
