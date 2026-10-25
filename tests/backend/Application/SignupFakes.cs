using Soro.Application.Subscription;
using Soro.Domain.Subscription;
using Soro.Application.Identity;
using Soro.Application.Signup;
using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Application.Tests;

/// <summary>Retient les e-mails « envoyés » : les tests y lisent le code comme le ferait le commerçant.</summary>
internal sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<(EmailAddress To, string Message)> _sent = [];

    public IReadOnlyList<(EmailAddress To, string Message)> Sent => _sent;

    public void Clear() => _sent.Clear();

    public string LastCode => new string(_sent[^1].Message.SkipWhile(c => !char.IsAsciiDigit(c)).TakeWhile(char.IsAsciiDigit).ToArray());

    /// <summary>Simule un serveur d'envoi en panne : le prochain envoi échoue.</summary>
    public bool Failing { get; set; }

    public Task SendAsync(EmailAddress recipient, EmailMessage message, CancellationToken cancellationToken)
    {
        if (Failing)
        {
            throw new EmailDeliveryException(new InvalidOperationException("serveur d'envoi indisponible"));
        }

        _sent.Add((recipient, $"{message.Subject}\n{message.Body}"));
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

internal sealed class InMemoryEmailVerificationRepository : IEmailVerificationRepository
{
    private readonly List<EmailVerification> _all = [];

    public IReadOnlyList<EmailVerification> All => _all;

    public Task<EmailVerification?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_all.FirstOrDefault(verification => verification.Id == id));

    public Task<EmailVerification?> FindLatestPendingAsync(
        EmailAddress email, VerificationPurpose purpose, CancellationToken cancellationToken) =>
        Task.FromResult(_all
            .Where(v => v.Email == email && v.Purpose == purpose && !v.IsConsumed)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefault());

    public Task<int> CountStartedSinceAsync(
        EmailAddress email, VerificationPurpose purpose, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult(_all.Count(v => v.Email == email && v.Purpose == purpose && v.CreatedAt >= since));

    public Task AddAsync(EmailVerification verification, CancellationToken cancellationToken)
    {
        _all.Add(verification);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(EmailVerification verification, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class InMemoryOwnerDirectory : IOwnerDirectory
{
    private readonly Dictionary<EmailAddress, OwnerAccount> _accounts = [];

    public int Count => _accounts.Count;

    public Task<OwnerAccount?> FindAsync(EmailAddress email, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.GetValueOrDefault(email));

    public Task AddAsync(OwnerAccount account, CancellationToken cancellationToken)
    {
        _accounts[account.Email] = account;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryCommerceRepository : ICommerceRepository
{
    private readonly Dictionary<TenantId, Commerce> _commerces = [];

    public int Count => _commerces.Count;

    public IReadOnlyCollection<Commerce> All => _commerces.Values;

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

internal sealed class FakeSubscriptionSettings : ISubscriptionSettings
{
    public long MonthlyPriceFcfa => 10_000;

    public long AnnualPriceFcfa => 100_000;

    public IReadOnlyDictionary<SubscriptionMethod, string> PayNumbers { get; } = new Dictionary<SubscriptionMethod, string>
    {
        [SubscriptionMethod.Wave] = "07 00 00 00 01",
        [SubscriptionMethod.OrangeMoney] = "07 00 00 00 02",
    };
}

/// <summary>Paiements d'abonnement et accès plateforme en mémoire ; partagé entre plusieurs commerces de test pour vérifier l'unicité des références.</summary>
internal sealed class InMemorySubscriptions : ISubscriptionPaymentRepository, IPlatformSubscriptions
{
    private readonly List<SubscriptionPayment> _payments = [];
    private readonly List<InMemoryCommerceRepository> _commerceSources = [];
    private readonly HashSet<(TenantId, DateTimeOffset, string)> _reminders = [];

    public IReadOnlyList<SubscriptionPayment> All => _payments;

    public void Register(InMemoryCommerceRepository commerces) => _commerceSources.Add(commerces);

    public Task<SubscriptionPayment?> GetAsync(SubscriptionPaymentId id, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_payments.FirstOrDefault(payment => payment.Id == id && payment.Tenant == tenant));

    public Task<IReadOnlyList<SubscriptionPayment>> ListAsync(TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionPayment>>(_payments.Where(payment => payment.Tenant == tenant).OrderByDescending(payment => payment.DeclaredAt).ToArray());

    public Task AddAsync(SubscriptionPayment payment, CancellationToken cancellationToken)
    {
        _payments.Add(payment);
        return Task.CompletedTask;
    }

    public Task<bool> IsReferenceUsedAsync(SubscriptionMethod method, string referenceKey, CancellationToken cancellationToken) =>
        Task.FromResult(_payments.Any(payment => payment.Method == method && payment.ReferenceKey == referenceKey));

    public async Task<IReadOnlyList<PlatformPayment>> ListPendingAsync(CancellationToken cancellationToken)
    {
        var commerces = await ListCommercesAsync(cancellationToken);
        return _payments
            .Where(payment => payment.Status == SubscriptionPaymentStatus.Pending)
            .Select(payment => new PlatformPayment(payment, commerces.First(commerce => commerce.Id == payment.Tenant)))
            .ToArray();
    }

    public async Task<PlatformPayment?> FindPaymentAsync(SubscriptionPaymentId id, CancellationToken cancellationToken)
    {
        var payment = _payments.FirstOrDefault(candidate => candidate.Id == id);
        if (payment is null)
        {
            return null;
        }

        var commerces = await ListCommercesAsync(cancellationToken);
        return new PlatformPayment(payment, commerces.First(commerce => commerce.Id == payment.Tenant));
    }

    public Task SaveDecisionAsync(SubscriptionPayment payment, Commerce commerce, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<Commerce>> ListCommercesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Commerce>>(_commerceSources.SelectMany(source => source.All).ToArray());

    public Task<bool> WasReminderSentAsync(TenantId tenant, DateTimeOffset endsAt, string kind, CancellationToken cancellationToken) =>
        Task.FromResult(_reminders.Contains((tenant, endsAt, kind)));

    public Task RecordReminderAsync(TenantId tenant, DateTimeOffset endsAt, string kind, DateTimeOffset at, CancellationToken cancellationToken)
    {
        _reminders.Add((tenant, endsAt, kind));
        return Task.CompletedTask;
    }
}
