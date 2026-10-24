using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Cash;
using Soro.Application.Sales;
using Soro.Application.Settings;
using Soro.Application.Reporting;
using Soro.Application.Stock;
using Soro.Domain.Stock;
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

    public void Clear() => _entries.Clear();

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

internal sealed class InMemoryProductRepository : IProductRepository
{
    private readonly List<Product> _products = [];

    public void Put(Product product) => _products.Add(product);

    public Task<Product?> GetAsync(ProductId id, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_products.FirstOrDefault(product => product.Id == id && product.Tenant == tenant));

    public Task<bool> IsBarcodeUsedAsync(
        TenantId tenant,
        string barcode,
        ProductId? except,
        CancellationToken cancellationToken) =>
        Task.FromResult(_products.Any(product =>
            product.Tenant == tenant && product.Barcode == barcode && product.Id != except));

    public Task<IReadOnlyList<Product>> ListAsync(
        TenantId tenant,
        string? search,
        bool includeInactive,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Product>>(_products
            .Where(product => product.Tenant == tenant && (includeInactive || product.IsActive))
            .Where(product => string.IsNullOrWhiteSpace(search)
                || product.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || product.Barcode == search)
            .OrderBy(product => product.Name)
            .ToArray());

    public Task<IReadOnlyList<Product>> ListLowStockAsync(TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Product>>(_products
            .Where(product => product.Tenant == tenant && product.IsActive && product.IsLowStock)
            .OrderBy(product => product.QuantityOnHand)
            .ToArray());

    public Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        _products.Add(product);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Product product, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class InMemoryStockMovementRepository : IStockMovementRepository
{
    private readonly List<StockMovement> _movements = [];

    public IReadOnlyList<StockMovement> All => _movements;

    public Task<bool> ExistsAsync(StockMovementId id, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_movements.Any(movement => movement.Id == id && movement.Tenant == tenant));

    public Task AddAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        _movements.Add(movement);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StockMovement>> ListForProductAsync(
        ProductId product,
        TenantId tenant,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StockMovement>>(_movements
            .Where(movement => movement.Product == product && movement.Tenant == tenant)
            .OrderByDescending(movement => movement.At)
            .Take(limit)
            .ToArray());
}

/// <summary>Enregistre les appels : la logique de requête est vérifiée contre PostgreSQL (tests d'intégration).</summary>
internal sealed class FakeReportingQueries : IReportingQueries
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls => _calls;

    public TenantId? LastTenant { get; private set; }

    public SalesFilter? LastSalesFilter { get; private set; }

    public Task<IReadOnlyList<SaleSummary>> ListSalesAsync(
        TenantId tenant, SalesFilter filter, CancellationToken cancellationToken)
    {
        Record(nameof(ListSalesAsync), tenant);
        LastSalesFilter = filter;
        return Task.FromResult<IReadOnlyList<SaleSummary>>([]);
    }

    public Task<SaleDetail?> GetSaleAsync(TenantId tenant, SaleId id, CancellationToken cancellationToken)
    {
        Record(nameof(GetSaleAsync), tenant);
        return Task.FromResult<SaleDetail?>(null);
    }

    public Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(
        TenantId tenant, SessionsFilter filter, CancellationToken cancellationToken)
    {
        Record(nameof(ListSessionsAsync), tenant);
        return Task.FromResult<IReadOnlyList<SessionSummary>>([]);
    }

    public RankingCriterion? LastCriterion { get; private set; }

    public int? LastLimit { get; private set; }

    public bool? LastIncludeMargin { get; private set; }

    public Task<IReadOnlyList<ProductRanking>> GetTopProductsAsync(
        TenantId tenant, DateTimeOffset periodStart, DateTimeOffset periodEnd, RankingCriterion criterion, int limit,
        bool includeMargin, CancellationToken cancellationToken)
    {
        Record(nameof(GetTopProductsAsync), tenant);
        LastCriterion = criterion;
        LastLimit = limit;
        LastIncludeMargin = includeMargin;
        return Task.FromResult<IReadOnlyList<ProductRanking>>([]);
    }

    public Task<ActivityReport> GetActivityAsync(
        TenantId tenant, DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken)
    {
        Record(nameof(GetActivityAsync), tenant);
        return Task.FromResult(new ActivityReport(periodStart, periodEnd, 0, 0, 0, 0, 0, [], [], 0, 0, 0));
    }

    private void Record(string call, TenantId tenant)
    {
        _calls.Add(call);
        LastTenant = tenant;
    }
}

internal sealed class FakeAuditLogQueries : IAuditLogQueries
{
    public TenantId? LastTenant { get; private set; }

    public AuditLogFilter? LastFilter { get; private set; }

    public int Calls { get; private set; }

    public Task<AuditPage> ListAsync(TenantId tenant, AuditLogFilter filter, CancellationToken cancellationToken)
    {
        Calls++;
        LastTenant = tenant;
        LastFilter = filter;
        return Task.FromResult(new AuditPage([], false));
    }
}
