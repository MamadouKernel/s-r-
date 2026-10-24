using Microsoft.Extensions.Logging;
using Soro.Application.Signup;
using Soro.Domain.Common;

namespace Soro.Infrastructure.Security;

/// <summary>
/// Adaptateur de développement : n'envoie rien, écrit le message dans le journal. À ne jamais utiliser en production (le serveur
/// refuse de démarrer sans réglages d'envoi, voir la configuration <c>Mail:Smtp</c>). L'adresse n'est pas masquée : en développement, c'est ce qui permet
/// de retrouver le code d'un compte de test.
/// </summary>
public sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailAddress recipient, EmailMessage message, CancellationToken cancellationToken)
    {
        Log(logger, recipient.Value, message.Subject, message.Body);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "E-mail de développement vers {Recipient} — {Subject}\n{Body}")]
    private static partial void Log(ILogger logger, string recipient, string subject, string body);
}
