using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Soro.Application.Signup;
using Soro.Domain.Common;
using Soro.Infrastructure.Security;

namespace Soro.Integration.Tests;

/// <summary>L'envoi réel par SMTP, contre un faux serveur local : la connexion, le message, et l'échec propre quand rien n'écoute.</summary>
public class SmtpEmailSenderTests
{
    /// <summary>Un serveur SMTP minimal : accepte un message et garde ce qu'il a reçu.</summary>
    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _loop;

        public FakeSmtpServer()
        {
            _listener.Start();
            _loop = Task.Run(AcceptAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string Received { get; private set; } = string.Empty;

        private async Task AcceptAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
                await writer.WriteLineAsync("220 fake ready");
                var data = false;
                var body = new StringBuilder();
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (data)
                    {
                        if (line == ".")
                        {
                            data = false;
                            Received = body.ToString();
                            await writer.WriteLineAsync("250 queued");
                        }
                        else
                        {
                            body.AppendLine(line);
                        }

                        continue;
                    }

                    var command = line.ToUpperInvariant();
                    if (command.StartsWith("EHLO", StringComparison.Ordinal) || command.StartsWith("HELO", StringComparison.Ordinal))
                    {
                        await writer.WriteLineAsync("250 fake");
                    }
                    else if (command.StartsWith("DATA", StringComparison.Ordinal))
                    {
                        data = true;
                        await writer.WriteLineAsync("354 go");
                    }
                    else if (command.StartsWith("QUIT", StringComparison.Ordinal))
                    {
                        await writer.WriteLineAsync("221 bye");
                        return;
                    }
                    else
                    {
                        await writer.WriteLineAsync("250 ok");
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Le test s'est terminé avant d'avoir parlé au serveur.
            }
            catch (SocketException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await Task.WhenAny(_loop, Task.Delay(2_000));
        }
    }

    private static SmtpEmailSender SenderFor(string host, int port) =>
        new(new SmtpSettings(host, port, null, null, "ne-pas-repondre@exemple.ci", "Sôrô", SecureSocketOptions.None), NullLogger<SmtpEmailSender>.Instance);

    [Fact]
    public async Task Sends_the_message_to_the_server_with_the_subject_and_the_code()
    {
        await using var server = new FakeSmtpServer();

        await SenderFor("127.0.0.1", server.Port).SendAsync(
            EmailAddress.Parse("awa@exemple.ci"), new EmailMessage("Votre code Sôrô", "Votre code est : 123456"), CancellationToken.None);

        Assert.Contains("awa@exemple.ci", server.Received);
        Assert.Contains("123456", server.Received);
        Assert.Contains("Subject:", server.Received);
        Assert.Contains("ne-pas-repondre@exemple.ci", server.Received);
    }

    [Fact]
    public async Task Reaches_a_server_given_by_name_even_though_the_name_has_an_ipv6_address_that_goes_nowhere()
    {
        // « localhost » a une adresse IPv4 et une IPv6 : l'IPv4 est essayée d'abord, le serveur n'écoute que dessus.
        await using var server = new FakeSmtpServer();

        await SenderFor("localhost", server.Port).SendAsync(
            EmailAddress.Parse("awa@exemple.ci"), new EmailMessage("Objet", "Texte 654321"), CancellationToken.None);

        Assert.Contains("654321", server.Received);
    }

    [Fact]
    public async Task A_server_that_is_not_there_becomes_a_clear_delivery_error_quickly_without_leaking_the_message()
    {
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop(); // plus personne n'écoute sur ce port

        var started = DateTime.UtcNow;
        var failure = await Assert.ThrowsAsync<EmailDeliveryException>(() => SenderFor("127.0.0.1", port).SendAsync(
            EmailAddress.Parse("awa@exemple.ci"), new EmailMessage("Objet", "Texte secret 999999"), CancellationToken.None));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        Assert.DoesNotContain("999999", failure.ToString().Replace(failure.InnerException?.ToString() ?? string.Empty, string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unknown_host_is_a_delivery_error_too()
    {
        await Assert.ThrowsAsync<EmailDeliveryException>(() => SenderFor("serveur-inexistant.invalid", 25).SendAsync(
            EmailAddress.Parse("awa@exemple.ci"), new EmailMessage("Objet", "Texte"), CancellationToken.None));
    }
}
