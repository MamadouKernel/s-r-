using Soro.Application.Identity;
using Soro.Application.Signup;
using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Application.Tests;

/// <summary>Retient les SMS « envoyés » : les tests y lisent le code comme le ferait le commerçant.</summary>
internal sealed class CapturingSmsSender : ISmsSender
{
    private readonly List<(PhoneNumber To, string Message)> _sent = [];

    public IReadOnlyList<(PhoneNumber To, string Message)> Sent => _sent;

    public string LastCode => new string(_sent[^1].Message.SkipWhile(c => !char.IsAsciiDigit(c)).TakeWhile(char.IsAsciiDigit).ToArray());

    public Task SendAsync(PhoneNumber recipient, string message, CancellationToken cancellationToken)
    {
        _sent.Add((recipient, message));
        return Task.CompletedTask;
    }
}

/// <summary>Codes prévisibles (111111, 222222…) et condensat lisible, pour tester sans hasard.</summary>
internal sealed class SequentialCodes : IVerificationCodes
{
    private int _next;

    /// <summary>111111, 222222, 333333… : chaque tirage est différent du précédent et lisible dans un test.</summary>
    public string Generate() => new((char)('0' + (_next++ % 9) + 1), 6);

    public string Hash(Guid verificationId, string code) => $"{verificationId:N}:{code}";

    public bool Verify(Guid verificationId, string code, string expectedHash) => Hash(verificationId, code) == expectedHash;
}

internal sealed class InMemoryPhoneVerificationRepository : IPhoneVerificationRepository
{
    private readonly List<PhoneVerification> _all = [];

    public IReadOnlyList<PhoneVerification> All => _all;

    public Task<PhoneVerification?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_all.FirstOrDefault(verification => verification.Id == id));

    public Task<PhoneVerification?> FindLatestPendingAsync(
        PhoneNumber phone, VerificationPurpose purpose, CancellationToken cancellationToken) =>
        Task.FromResult(_all
            .Where(v => v.Phone == phone && v.Purpose == purpose && !v.IsConsumed)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefault());

    public Task<int> CountStartedSinceAsync(
        PhoneNumber phone, VerificationPurpose purpose, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult(_all.Count(v => v.Phone == phone && v.Purpose == purpose && v.CreatedAt >= since));

    public Task AddAsync(PhoneVerification verification, CancellationToken cancellationToken)
    {
        _all.Add(verification);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PhoneVerification verification, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class InMemoryOwnerDirectory : IOwnerDirectory
{
    private readonly Dictionary<PhoneNumber, OwnerAccount> _accounts = [];

    public int Count => _accounts.Count;

    public Task<OwnerAccount?> FindAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.GetValueOrDefault(phone));

    public Task AddAsync(OwnerAccount account, CancellationToken cancellationToken)
    {
        _accounts[account.Phone] = account;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryCommerceRepository : ICommerceRepository
{
    private readonly Dictionary<TenantId, Commerce> _commerces = [];

    public int Count => _commerces.Count;

    public Task<Commerce?> GetAsync(TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_commerces.GetValueOrDefault(tenant));

    public Task AddAsync(Commerce commerce, CancellationToken cancellationToken)
    {
        _commerces[commerce.Id] = commerce;
        return Task.CompletedTask;
    }
}

internal sealed class FakeSignupSettings : ISignupSettings
{
    public string CurrentTermsVersion => "2026-10-brouillon";

    public int TrialDays => 30;
}

/// <summary>Condensat lisible, avec le compte des « vérifications factices » faites pour un compte inexistant (égalisation du temps de réponse).</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public int DummyVerifications { get; private set; }

    public string Hash(string password) => $"pwd-hash:{password}";

    public bool Verify(string password, string? hash)
    {
        if (hash is null)
        {
            DummyVerifications++;
            return false;
        }

        return hash == Hash(password);
    }
}
