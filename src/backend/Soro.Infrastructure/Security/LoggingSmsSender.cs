using Microsoft.Extensions.Logging;
using Soro.Application.Signup;
using Soro.Domain.Common;

namespace Soro.Infrastructure.Security;

/// <summary>
/// Adaptateur de développement : n'envoie rien, écrit le message dans le journal. À ne jamais utiliser en production (le serveur
/// refuse de démarrer sans fournisseur réel, voir la configuration <c>Sms:Provider</c>). Le fournisseur réel reste à choisir (ADR 0005).
/// </summary>
public sealed partial class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(PhoneNumber recipient, string message, CancellationToken cancellationToken)
    {
        Log(logger, recipient.Masked, message);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMS de développement vers {Recipient} : {Message}")]
    private static partial void Log(ILogger logger, string recipient, string message);
}
