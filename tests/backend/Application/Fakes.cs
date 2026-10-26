using Soro.Application.Export;
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

    public void Reset() => Commits = 0;

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

    public Task<IReadOnlyList<string>> RecentInternalBarcodesAsync(TenantId tenant, int count, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(_products
            .Where(product => product.Tenant == tenant && product.Barcode is { Length: 13 } code && code.StartsWith("29", StringComparison.Ordinal))
            .Select(product => product.Barcode!)
            .OrderByDescending(code => code, StringComparer.Ordinal)
            .Take(count)
            .ToArray());

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

    public Task<IReadOnlyList<Product>> ListByFamilyAsync(FamilyId family, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Product>>(_products
            .Where(product => product.Tenant == tenant && product.FamilyId == family)
            .ToArray());

    public Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        _products.Add(product);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Product product, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class InMemoryProductFamilyRepository : IProductFamilyRepository
{
    private readonly List<ProductFamily> _families = [];

    public void Put(ProductFamily family) => _families.Add(family);

    public void Remove(FamilyId id) => _families.RemoveAll(family => family.Id == id);

    public Task<ProductFamily?> GetAsync(FamilyId id, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_families.FirstOrDefault(family => family.Id == id && family.Tenant == tenant));

    public Task<IReadOnlyList<ProductFamily>> ListAsync(TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProductFamily>>(_families.Where(family => family.Tenant == tenant).ToArray());

    public Task AddAsync(ProductFamily family, CancellationToken cancellationToken)
    {
        _families.Add(family);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ProductFamily family, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class InMemoryPhotoStore : IPhotoStore
{
    private readonly List<Photo> _photos = [];

    public int Count => _photos.Count;

    public IReadOnlyList<Photo> All => _photos;

    public void Put(Photo photo) => _photos.Add(photo);

    public Task<Photo?> GetAsync(PhotoId id, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(_photos.FirstOrDefault(photo => photo.Id == id && photo.Tenant == tenant));

    public Task AddAsync(Photo photo, CancellationToken cancellationToken)
    {
        _photos.Add(photo);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(PhotoId id, TenantId tenant, CancellationToken cancellationToken)
    {
        _photos.RemoveAll(photo => photo.Id == id && photo.Tenant == tenant);
        return Task.CompletedTask;
    }
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

    /// <summary>Ce que le journal renvoie (par défaut : rien).</summary>
    public IReadOnlyList<AuditEntryView> Next { get; set; } = [];

    public Task<AuditPage> ListAsync(TenantId tenant, AuditLogFilter filter, CancellationToken cancellationToken)
    {
        Calls++;
        LastTenant = tenant;
        LastFilter = filter;
        return Task.FromResult(new AuditPage(Next.Take(filter.Limit).ToArray(), false));
    }
}

internal sealed class FakeSheetReader : IProductSheetReader
{
    public SheetRead Next { get; set; } = new(null, []);

    public int Calls { get; private set; }

    public SheetRead Read(byte[] file)
    {
        Calls++;
        return Next;
    }
}

internal sealed class FakeTemplateWriter : IProductTemplateWriter
{
    public byte[] Bytes { get; } = [1, 2, 3];

    public byte[] Write() => Bytes;
}

internal sealed class FakeDataExportQueries : IDataExportQueries
{
    public ExportCounts Counts { get; set; } = new(1, 1, 1, 1, 1, 1);

    public int Reads { get; private set; }

    public TenantId? LastTenant { get; private set; }

    public Task<ExportCounts> CountAsync(TenantId tenant, ExportPeriod period, CancellationToken cancellationToken)
    {
        LastTenant = tenant;
        return Task.FromResult(Counts);
    }

    public Task<DataExportData> ReadAsync(TenantId tenant, ExportPeriod period, CancellationToken cancellationToken)
    {
        Reads++;
        LastTenant = tenant;
        return Task.FromResult(new DataExportData([], [], [], [], [], []));
    }
}

internal sealed class FakeDataExportWriter : IDataExportWriter
{
    public byte[] Bytes { get; } = [9, 8, 7];

    public DataExport? Last { get; private set; }

    public int Writes { get; private set; }

    public byte[] Write(DataExport data)
    {
        Writes++;
        Last = data;
        return Bytes;
    }
}
