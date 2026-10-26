using System.Net;
using Microsoft.Extensions.Configuration;
using Soro.Api;

namespace Soro.Integration.Tests;

[Collection(Postgres.Collection)]
public sealed class ApiHardeningTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Every_response_carries_the_browser_protection_headers_even_a_refusal()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)).Build();

    [Fact]
    public void Without_a_trusted_proxy_the_forwarded_address_is_never_believed()
    {
        Assert.Null(ForwardedHeadersSetup.OptionsFrom(Config()));
    }

    [Fact]
    public void A_configured_proxy_is_trusted_and_only_that_one()
    {
        var options = ForwardedHeadersSetup.OptionsFrom(Config(("ForwardedHeaders:KnownProxies:0", "10.0.0.5"), ("ForwardedHeaders:KnownProxies:1", "::1")));

        Assert.NotNull(options);
        Assert.Equal(["10.0.0.5", "::1"], options.KnownProxies.Select(address => address.ToString()).ToArray());
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(1, options.ForwardLimit);
    }

    [Fact]
    public void A_garbled_proxy_address_stops_the_server_instead_of_silently_trusting_nothing()
    {
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.OptionsFrom(Config(("ForwardedHeaders:KnownProxies:0", "pas-une-adresse"))));
    }
}

[Collection(Postgres.Collection)]
public sealed class DatabaseRoleGuardTests(PostgresFixture fixture)
{
    [Fact]
    public async Task The_application_role_is_accepted()
    {
        Assert.Null(await Soro.Infrastructure.Persistence.DatabaseRoleGuard.FindProblemAsync(fixture.AppConnectionString("guard"), CancellationToken.None));
    }

    [Fact]
    public async Task A_superuser_role_is_refused_with_a_plain_reason()
    {
        var problem = await Soro.Infrastructure.Persistence.DatabaseRoleGuard.FindProblemAsync(fixture.OwnerConnectionString, CancellationToken.None);

        Assert.Contains("super-utilisateur", problem);
    }
}
