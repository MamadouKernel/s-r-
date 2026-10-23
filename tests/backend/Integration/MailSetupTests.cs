using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Soro.Api;
using Soro.Application.Signup;
using Soro.Infrastructure.Security;

namespace Soro.Integration.Tests;

/// <summary>Le serveur ne doit jamais démarrer en production avec un envoi de codes qui n'envoie rien.</summary>
public class MailSetupTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Soro";

        public string ContentRootPath { get; set; } = ".";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceCollection Register(string environment, Dictionary<string, string?> settings)
    {
        settings["Security:PinPepper"] = Convert.ToBase64String(new byte[32]);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSoroApplication(configuration, new FakeEnvironment(environment));
        return services;
    }

    [Fact]
    public void Production_refuses_to_start_without_a_mail_server() =>
        Assert.Contains("Mail:Smtp:Host", Assert.Throws<InvalidOperationException>(() => Register("Production", [])).Message);

    [Fact]
    public void Development_without_a_mail_server_writes_the_messages_to_the_log()
    {
        using var provider = Register("Development", []).BuildServiceProvider();

        Assert.IsType<LoggingEmailSender>(provider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void A_configured_mail_server_is_used_even_in_development()
    {
        using var provider = Register("Development", new()
        {
            ["Mail:Smtp:Host"] = "smtp.exemple.ci",
            ["Mail:Smtp:FromAddress"] = "ne-pas-repondre@exemple.ci",
        }).BuildServiceProvider();

        Assert.IsType<SmtpEmailSender>(provider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void A_mail_server_without_a_valid_sender_address_is_refused() =>
        Assert.Contains("FromAddress", Assert.Throws<InvalidOperationException>(() => Register("Production", new()
        {
            ["Mail:Smtp:Host"] = "smtp.exemple.ci",
            ["Mail:Smtp:FromAddress"] = "pas-une-adresse",
        })).Message);
}
