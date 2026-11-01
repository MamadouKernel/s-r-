using System.Net;
using Npgsql;

namespace Soro.Integration.Tests;

/// <summary>Le point de santé (docs/specs/exploitation EXP-05) : lu par la surveillance extérieure, sans compte, sans rien révéler.</summary>
[Collection(Postgres.Collection)]
public sealed class ApiHealthTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Live_answers_without_any_account_while_the_server_runs()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_answers_ok_without_any_account_when_the_database_answers()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", (await response.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task Ready_says_unavailable_when_the_database_does_not_answer_and_reveals_nothing_about_it()
    {
        var unreachable = new NpgsqlConnectionStringBuilder(fixture.AppConnectionString("health"))
        {
            Host = "127.0.0.1",
            Port = 1,
            Password = "mot-de-passe-secret",
            Timeout = 2,
        }.ConnectionString;
        using var broken = new ApiFactory(fixture, settings: new Dictionary<string, string>
        {
            ["ConnectionStrings:Soro"] = unreachable,
            ["Database:AllowPrivilegedRole"] = "true",
        });
        using var client = broken.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("indisponible", body.Trim());
        Assert.DoesNotContain("mot-de-passe-secret", body);
        Assert.DoesNotContain("127.0.0.1", body);
        // Le serveur lui-même tourne : « live » reste vert, c'est « ready » qui alerte.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task The_probe_is_never_cached_by_a_proxy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }
}
