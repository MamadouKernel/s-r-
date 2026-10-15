using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Cash;
using Soro.Application.Sales;
using Soro.Application.Settings;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

// Doublures en mémoire des ports : elles filtrent par tenant comme le fera l'infrastructure (RG-15).

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = TestWorld.Morning.AddHours(1);
}

internal sealed class FakeCurrentUser(TenantId tenant, UserId user, Role role) : ICurrentUser
{
    public TenantId Tenant { get; } = tenant;

    public UserId User { get; private set; } = user;

    public Role Role { get; private set; } = role;

    public void Become(UserId user, Role role)
    {
        User = user;
        Role = role;
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Commits++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeShopSettings : IShopSettings
{
    public Task<ShopSettings> GetAsync(TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(ShopSettings.Default);
}

internal sealed class InMemoryUserDirectory : IUserDirectory
{
    private readonly Dictionary<(TenantId, UserId), Role> _roles = [];

    public void Set(TenantId tenant, UserId user, Role role) => _roles[(tenant, user)] = role;

    public Task<Role?> GetRoleAsync(UserId user, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_roles.TryGetValue((tenant, user), out var role) ? (Role?)role : null);
}

internal sealed class RecordingAuditTrail : IAuditTrail
{
    private readonly List<AuditEntry> _entries = [];

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }
}

internal sealed class InMemorySessionRepository : ISessionRepository
{
    private readonly Dictionary<SessionId, CashRegisterSession> _sessions = [];

    public int Count => _sessions.Count;

    public void Put(CashRegisterSession session) => _sessions[session.Id] = session;

    public Task<CashRegisterSession?> GetAsync(SessionId id, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.GetValueOrDefault(id) is { } found && found.Assignment.Tenant == tenant ? found : null);

    public Task<CashRegisterSession?> FindOpenForRegisterAsync(
        RegisterId registerId,
        TenantId tenant,
        CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.Values.FirstOrDefault(session =>
            session.Assignment.Tenant == tenant
            && session.Assignment.Register == registerId
            && session.Status == SessionStatus.Open));

    public Task AddAsync(CashRegisterSession session, CancellationToken cancellationToken)
    {
        Put(session);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CashRegisterSession session, CancellationToken cancellationToken)
    {
        Put(session);
        return Task.CompletedTask;
    }
}

internal sealed class InMemorySaleRepository : ISaleRepository
{
    private readonly List<Sale> _sales = [];

    public IReadOnlyList<Sale> All => _sales;

    public void Put(Sale sale) => _sales.Add(sale);

    public Task<Sale?> GetAsync(SaleId id, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_sales.FirstOrDefault(sale => sale.Id == id && sale.Assignment.Tenant == tenant));

    public Task<bool> IsMobileMoneyReferenceUsedAsync(
        TenantId tenant,
        PaymentMethod method,
        string reference,
        CancellationToken cancellationToken) =>
        Task.FromResult(_sales
            .Where(sale => sale.Assignment.Tenant == tenant)
            .SelectMany(sale => sale.Payments)
            .Any(payment => payment.Method == method
                && string.Equals(payment.Reference, reference, StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(Sale sale, CancellationToken cancellationToken)
    {
        _sales.Add(sale);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Sale sale, CancellationToken cancellationToken)
    {
        var index = _sales.FindIndex(existing => existing.Id == sale.Id);
        _sales[index] = sale;
        return Task.CompletedTask;
    }
}
