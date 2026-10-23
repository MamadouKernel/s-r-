using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Soro.Application.Signup;
using Soro.Domain.Common;

namespace Soro.Infrastructure.Security;

/// <summary>Réglages du serveur d'envoi (SMTP). Aucun secret par défaut : tout vient de la configuration du serveur.</summary>
public sealed record SmtpSettings(
    string Host,
    int Port,
    string? UserName,
    string? Password,
    string FromAddress,
    string FromName,
    SecureSocketOptions Security);

/// <summary>
/// Envoi réel par SMTP avec MailKit (ADR 0005). Une connexion par message : le volume (inscriptions, mots de passe oubliés)
/// est faible et plafonné. Un échec est journalisé sans le contenu du message (il porte le code) et remonté en
/// <see cref="EmailDeliveryException"/>, que les cas d'usage transforment en message clair.
/// </summary>
public sealed partial class SmtpEmailSender(SmtpSettings settings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task SendAsync(EmailAddress recipient, EmailMessage message, CancellationToken cancellationToken)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(recipient.Value));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        try
        {
            using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
            await client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);
            if (!string.IsNullOrEmpty(settings.UserName))
            {
                await client.AuthenticateAsync(settings.UserName, settings.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailure(logger, exception, recipient.Masked);
            throw new EmailDeliveryException(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec d'envoi d'un e-mail vers {Recipient}")]
    private static partial void LogFailure(ILogger logger, Exception exception, string recipient);
}
