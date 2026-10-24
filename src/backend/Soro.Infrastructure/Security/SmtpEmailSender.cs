using System.Net;
using System.Net.Sockets;
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
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

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
            // Connexion ouverte à la main : certains réseaux mettent 20 secondes à répondre à une recherche IPv6 alors que l'IPv4 répond en quelques
            // millisecondes. La résolution par défaut attend les deux et dépasse le délai ; ici l'IPv4 passe d'abord, chaque étape a son délai.
            var socket = await OpenSocketAsync(settings.Host, settings.Port, cancellationToken);
            await client.ConnectAsync(socket, settings.Host, settings.Port, settings.Security, cancellationToken);
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

    internal static async Task<Socket> OpenSocketAsync(string host, int port, CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (var family in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 })
        {
            IPAddress[] addresses;
            try
            {
                using var lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lookup.CancelAfter(LookupTimeout);
                addresses = await Dns.GetHostAddressesAsync(host, family, lookup.Token);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                last = exception;
                continue;
            }

            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    attempt.CancelAfter(ConnectTimeout);
                    await socket.ConnectAsync(address, port, attempt.Token);
                    return socket;
                }
                catch (Exception exception) when (exception is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    socket.Dispose();
                    last = exception;
                }
            }
        }

        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec d'envoi d'un e-mail vers {Recipient}")]
    private static partial void LogFailure(ILogger logger, Exception exception, string recipient);
}
