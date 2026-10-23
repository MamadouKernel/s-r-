using Soro.Domain.Tenancy;

namespace Soro.Application.Signup;

/// <summary>
/// Textes des e-mails, en français simple. Le code n'apparaît que dans l'e-mail, jamais dans une réponse de l'API ni dans un journal
/// en production. L'objet ne contient aucun chiffre ni le code : il s'affiche dans l'aperçu de la boîte de réception.
/// </summary>
internal static class SignupMessages
{
    private static string Lifetime => $"{(int)EmailVerification.CodeLifetime.TotalMinutes} minutes";

    public static EmailMessage Verification(string code) => new(
        "Votre code de vérification Sôrô",
        $"Bonjour,\n\nVotre code de vérification Sôrô est : {code}\n\nIl est valable {Lifetime}. "
        + "Saisissez-le dans l'application pour terminer votre inscription.\n\n"
        + "Ne le communiquez à personne. Si vous n'avez rien demandé, ignorez simplement ce message.\n\nL'équipe Sôrô");

    public static EmailMessage PasswordReset(string code) => new(
        "Changer votre mot de passe Sôrô",
        $"Bonjour,\n\nPour choisir un nouveau mot de passe, saisissez ce code dans l'application : {code}\n\n"
        + $"Il est valable {Lifetime}.\n\n"
        + "Ne le communiquez à personne. Si vous n'avez rien demandé, ignorez simplement ce message : votre mot de passe actuel reste valable.\n\nL'équipe Sôrô");
}
