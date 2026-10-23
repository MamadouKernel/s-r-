using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

internal sealed class InMemoryUserRepository : IUserRepository
{
    private readonly Dictionary<UserId, User> _users = [];

    public int Count => _users.Count;

    public void Put(User user) => _users[user.Id] = user;

    public Task<User?> GetAsync(UserId id, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_users.GetValueOrDefault(id) is { } found && found.Tenant == tenant ? found : null);

    public Task<User?> FindByIdentifierAsync(TenantId tenant, string identifier, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Values.FirstOrDefault(user => user.Tenant == tenant && user.Identifier == identifier));

    public Task<IReadOnlyList<User>> ListAsync(TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<User>>(_users.Values.Where(user => user.Tenant == tenant).ToArray());

    public Task AddAsync(User user, CancellationToken cancellationToken)
    {
        Put(user);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        Put(user);
        return Task.CompletedTask;
    }
}

/// <summary>Condensat déterministe : suffisant pour tester la logique sans le coût d'un vrai hachage.</summary>
internal sealed class FakePinHasher : IPinHasher
{
    /// <summary>Nombre de vérifications factices faites pour un compte inexistant (égalisation du temps de réponse).</summary>
    public int DummyVerifications { get; private set; }

    public string Hash(string pin) => $"hash:{pin}";

    public bool Verify(string pin, string? hash)
    {
        if (hash is null)
        {
            DummyVerifications++;
            return false;
        }

        return hash == Hash(pin);
    }
}

internal sealed class FakeTokenIssuer : ITokenIssuer
{
    public AccessToken Issue(TenantId tenant, UserId user, Role role, DateTimeOffset now) =>
        new($"token:{tenant.Value}:{user.Value}:{role}", now.AddHours(8));
}

internal sealed class FakeTenantScope : ITenantScope
{
    public TenantId? Entered { get; private set; }

    public void Enter(TenantId tenant) => Entered = tenant;
}
