using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Soro.Domain.Common;

namespace Soro.Integration.Tests;

/// <summary>L'inscription de bout en bout : API réelle, PostgreSQL réel, e-mail capturé (SC-I1 à SC-I11).</summary>
[Collection(Postgres.Collection)]
public sealed class ApiSignupTests(PostgresFixture fixture) : IDisposable
{
    private static readonly string Password = string.Join('-', "Soro", "caisse", "2026");
    private static readonly string WrongPassword = string.Join('-', "Mauvais", "mot", "2026");
    private static readonly string NewPassword = string.Join('-', "Nouveau", "mot", "2027");
    private const string Terms = "2026-10-brouillon";

    private readonly ApiFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    /// <summary>Une adresse propre à chaque test : la base est partagée par toute la collection.</summary>
    private static string NewEmail() => $"client{Random.Shared.Next(10_000_000, 99_999_999)}@exemple.ci";

    private static object StartBody(string email, string commerce = "Supérette du Carrefour", string terms = Terms) =>
        new { email, commerceName = commerce, activity = "Supermarket", ownerName = "Mamadou Konaté", termsVersion = terms };

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await JsonOf(response)).GetProperty("code").GetString());
    }

    private async Task<(HttpClient Client, JsonElement Session)> SignUpAsync(string email, string commerce = "Supérette du Carrefour")
    {
        var client = _factory.CreateClient();
        var started = await client.PostAsJsonAsync("/api/v1/signup/start", StartBody(email, commerce));
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var id = (await JsonOf(started)).GetProperty("verificationId").GetGuid();
        var done = await client.PostAsJsonAsync(
            "/api/v1/signup/complete",
            new { verificationId = id, code = _factory.Mail.LastCodeFor(email), password = Password });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        return (client, await JsonOf(done));
    }

    [Fact]
    public async Task A_trader_signs_up_with_a_code_received_by_email_and_is_logged_in_as_the_owner()
    {
        var email = NewEmail();

        var (client, session) = await SignUpAsync(email);

        Assert.Equal("Owner", session.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal("Supérette du Carrefour", session.GetProperty("commerceName").GetString());
        Assert.True(session.GetProperty("trialEndsAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(29));
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        var team = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.OK, team.StatusCode);
        var members = (await JsonOf(team)).GetProperty("users");
        Assert.Equal(1, members.GetArrayLength());
        Assert.True(members[0].GetProperty("isSelf").GetBoolean());
    }

    [Fact]
    public async Task Nothing_is_created_before_the_code_is_confirmed_and_the_code_never_travels_in_a_response()
    {
        var email = NewEmail();
        using var client = _factory.CreateClient();

        var started = await client.PostAsJsonAsync("/api/v1/signup/start", StartBody(email));
        var body = await started.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Assert.DoesNotContain(_factory.Mail.LastCodeFor(email), body);
        await using var owner = Db.Owner(fixture);
        var registered = await owner.Database
            .SqlQuery<long>($"SELECT count(*)::bigint AS \"Value\" FROM owner_directory WHERE email = {EmailAddress.Parse(email).Value}")
            .SingleAsync();
        Assert.Equal(0, registered);
    }

    [Fact]
    public async Task A_wrong_code_is_refused_and_the_good_one_still_works()
    {
        var email = NewEmail();
        using var client = _factory.CreateClient();
        var id = (await JsonOf(await client.PostAsJsonAsync("/api/v1/signup/start", StartBody(email)))).GetProperty("verificationId").GetGuid();
        var good = _factory.Mail.LastCodeFor(email);
        var wrong = good == "000000" ? "000001" : "000000";

        var refused = await client.PostAsJsonAsync("/api/v1/signup/complete", new { verificationId = id, code = wrong, password = Password });
        var accepted = await client.PostAsJsonAsync("/api/v1/signup/complete", new { verificationId = id, code = good, password = Password });

        await AssertProblemAsync(refused, HttpStatusCode.UnprocessableEntity, "invalid_code");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task A_number_that_already_has_an_account_cannot_sign_up_again()
    {
        var email = NewEmail();
        await SignUpAsync(email);
        using var client = _factory.CreateClient();

        var again = await client.PostAsJsonAsync("/api/v1/signup/start", StartBody(email.ToUpperInvariant()));

        await AssertProblemAsync(again, HttpStatusCode.Conflict, "email_already_registered");
    }

    [Fact]
    public async Task Terms_that_are_not_the_current_ones_and_a_weak_password_are_unprocessable()
    {
        var email = NewEmail();
        using var client = _factory.CreateClient();

        var oldTerms = await client.PostAsJsonAsync("/api/v1/signup/start", StartBody(email, terms: "2020-01"));
        var id = (await JsonOf(await client.PostAsJsonAsync("/api/v1/signup/start", StartBody(email)))).GetProperty("verificationId").GetGuid();
        var weak = await client.PostAsJsonAsync("/api/v1/signup/complete", new { verificationId = id, code = _factory.Mail.LastCodeFor(email), password = "court1" });

        await AssertProblemAsync(oldTerms, HttpStatusCode.UnprocessableEntity, "rule_violation");
        await AssertProblemAsync(weak, HttpStatusCode.UnprocessableEntity, "rule_violation");
    }

    [Fact]
    public async Task A_malformed_body_is_a_bad_request()
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/v1/signup/start", content)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/v1/auth/owner/login", content)).StatusCode);
    }

    [Fact]
    public async Task The_owner_logs_in_with_the_email_and_the_password_and_every_failure_looks_alike()
    {
        var email = NewEmail();
        await SignUpAsync(email);
        using var client = _factory.CreateClient();

        var good = await client.PostAsJsonAsync("/api/v1/auth/owner/login", new { email = email.ToUpperInvariant(), password = Password });
        var wrong = await client.PostAsJsonAsync("/api/v1/auth/owner/login", new { email, password = WrongPassword });
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/owner/login", new { email = NewEmail(), password = Password });

        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
        Assert.Equal("Owner", (await JsonOf(good)).GetProperty("user").GetProperty("role").GetString());
        await AssertProblemAsync(wrong, HttpStatusCode.Unauthorized, "invalid_credentials");
        await AssertProblemAsync(unknown, HttpStatusCode.Unauthorized, "invalid_credentials");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        var email = NewEmail();
        await SignUpAsync(email);
        using var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/owner/login", new { email, password = WrongPassword });
        }

        var locked = await client.PostAsJsonAsync("/api/v1/auth/owner/login", new { email, password = Password });

        await AssertProblemAsync(locked, HttpStatusCode.Locked, "account_locked");
    }

    [Fact]
    public async Task A_forgotten_password_is_reset_with_a_code_and_the_old_one_stops_working()
    {
        var email = NewEmail();
        await SignUpAsync(email);
        using var client = _factory.CreateClient();

        var started = await client.PostAsJsonAsync("/api/v1/owner/password-reset/start", new { email });
        var unknown = await client.PostAsJsonAsync("/api/v1/owner/password-reset/start", new { email = NewEmail() });
        var confirmed = await client.PostAsJsonAsync(
            "/api/v1/owner/password-reset/confirm",
            new { email, code = _factory.Mail.LastCodeFor(email), newPassword = NewPassword });
        var old = await client.PostAsJsonAsync("/api/v1/auth/owner/login", new { email, password = Password });
        var fresh = await client.PostAsJsonAsync("/api/v1/auth/owner/login", new { email, password = NewPassword });

        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode); // SC-I9
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Fact]
    public async Task Resetting_the_password_ends_the_session_of_a_stolen_token()
    {
        var email = NewEmail();
        var (stolen, session) = await SignUpAsync(email);
        stolen.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await stolen.GetAsync("/api/v1/users")).StatusCode);
        await Task.Delay(TimeSpan.FromMilliseconds(2100)); // un jeton ne porte que des secondes entières : le changement doit être d'une seconde au moins plus récent
        using var owner = _factory.CreateClient();

        await owner.PostAsJsonAsync("/api/v1/owner/password-reset/start", new { email });
        await owner.PostAsJsonAsync(
            "/api/v1/owner/password-reset/confirm",
            new { email, code = _factory.Mail.LastCodeFor(email), newPassword = NewPassword });

        await AssertProblemAsync(await stolen.GetAsync("/api/v1/users"), HttpStatusCode.Unauthorized, "session_outdated");
        var fresh = await owner.PostAsJsonAsync("/api/v1/auth/owner/login", new { email, password = NewPassword });
        using var after = _factory.CreateClient();
        after.DefaultRequestHeaders.Authorization = new("Bearer", (await JsonOf(fresh)).GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await after.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task Two_commerces_stay_apart_and_each_owner_only_sees_their_own_team_and_commerce_row()
    {
        var emailA = NewEmail();
        var emailB = NewEmail();
        var (clientA, sessionA) = await SignUpAsync(emailA, "Commerce A");
        var (_, sessionB) = await SignUpAsync(emailB, "Commerce B");
        clientA.DefaultRequestHeaders.Authorization = new("Bearer", sessionA.GetProperty("accessToken").GetString());
        var tenantA = new TenantId(sessionA.GetProperty("tenantId").GetGuid());
        var tenantB = new TenantId(sessionB.GetProperty("tenantId").GetGuid());

        Assert.NotEqual(tenantA, tenantB);
        await using var asA = Db.App(fixture, tenantA);
        var names = await asA.Database.SqlQuery<string>($"SELECT name AS \"Value\" FROM commerces").ToListAsync();
        Assert.Equal(["Commerce A"], names); // RLS : la fiche de B est invisible
        var team = (await JsonOf(await clientA.GetAsync("/api/v1/users"))).GetProperty("users");
        Assert.Equal(1, team.GetArrayLength());
    }

    [Fact]
    public async Task The_creation_of_the_commerce_is_audited_and_the_password_is_stored_only_as_a_hash()
    {
        var email = NewEmail();
        var (_, session) = await SignUpAsync(email);
        var tenant = new TenantId(session.GetProperty("tenantId").GetGuid());

        await using var asOwner = Db.App(fixture, tenant);
        var actions = await asOwner.Database.SqlQuery<string>($"SELECT action AS \"Value\" FROM audit_entries").ToListAsync();
        var hash = await asOwner.Database.SqlQuery<string>($"SELECT password_hash AS \"Value\" FROM users").SingleAsync();

        Assert.Contains("commerce.created", actions);
        Assert.StartsWith("pbkdf2-sha256$", hash);
        Assert.DoesNotContain(Password, hash);
    }

    [Fact]
    public async Task Two_simultaneous_completions_for_the_same_number_create_one_account()
    {
        var email = NewEmail();
        using var client = _factory.CreateClient();
        var first = (await JsonOf(await client.PostAsJsonAsync("/api/v1/signup/start", StartBody(email)))).GetProperty("verificationId").GetGuid();
        var firstCode = _factory.Mail.LastCodeFor(email);
        var second = (await JsonOf(await client.PostAsJsonAsync("/api/v1/signup/start", StartBody(email)))).GetProperty("verificationId").GetGuid();
        var secondCode = _factory.Mail.LastCodeFor(email);

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/v1/signup/complete", new { verificationId = first, code = firstCode, password = Password }),
            client.PostAsJsonAsync("/api/v1/signup/complete", new { verificationId = second, code = secondCode, password = Password }));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode is HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task The_public_routes_are_rate_limited_per_address()
    {
        using var limited = new ApiFactory(fixture, publicPermitsPerMinute: 3);
        using var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/v1/owner/password-reset/start", new { email = NewEmail() })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Accepted, statuses[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }
}
