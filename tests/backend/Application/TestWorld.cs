using Soro.Application.Cash;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

/// <summary>Un commerce de test : un caissier, un gérant, des dépôts en mémoire et les cas d'usage câblés.</summary>
internal sealed class TestWorld
{
    public static readonly DateTimeOffset Morning = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    public TestWorld()
    {
        Directory.Set(Tenant, Cashier, Role.Cashier);
        Directory.Set(Tenant, Manager, Role.Manager);
        Current = new FakeCurrentUser(Tenant, Cashier, Role.Cashier);
    }

    public TenantId Tenant { get; } = TenantId.New();

    public UserId Cashier { get; } = UserId.New();

    public UserId Manager { get; } = UserId.New();

    public UserId Owner { get; } = UserId.New();

    public FakeCurrentUser Current { get; }

    public FakeClock Clock { get; } = new();

    public FakeUnitOfWork UnitOfWork { get; } = new();

    public InMemoryUserDirectory Directory { get; } = new();

    public InMemorySessionRepository Sessions { get; } = new();

    public InMemorySaleRepository Sales { get; } = new();

    public RecordingAuditTrail Audit { get; } = new();

    public InMemoryUserRepository Users { get; } = new();

    public FakePinHasher Hasher { get; } = new();

    public FakeTenantScope Scope { get; } = new();

    private FakeTokenIssuer Tokens { get; } = new();

    private FakeShopSettings Settings { get; } = new();

    public OpenRegisterSessionHandler OpenSession => new(Current, Sessions, UnitOfWork);

    public CloseRegisterSessionHandler CloseSession => new(Current, Sessions, UnitOfWork);

    public RecordSaleHandler RecordSale => new(Current, Sessions, Sales, Settings, Directory, Audit, Clock, UnitOfWork);

    public SyncSalesHandler SyncSales => new(RecordSale);

    public CancelSaleHandler CancelSale => new(Current, Sales, Audit, Clock, UnitOfWork);

    public LogInHandler LogIn => new(Scope, Users, Hasher, Tokens, Clock, UnitOfWork);

    public RegisterUserHandler RegisterUser => new(Current, Users, Hasher, Clock, UnitOfWork);

    public DisableUserHandler DisableUser => new(Current, Users, Audit, Clock, UnitOfWork);

    public void ActAs(UserId user, Role role) => Current.Become(user, role);

    public User GivenUser(
        string identifier = "awa.kone",
        string pin = "1234",
        Role role = Role.Cashier,
        TenantId? tenant = null)
    {
        var user = User.Register(
            UserId.New(), tenant ?? Tenant, identifier, "Awa Koné", role, Hasher.Hash(pin), Clock.UtcNow.AddDays(-1));
        Users.Put(user);
        return user;
    }

    public CashRegisterSession GivenOpenSession(UserId? cashier = null, TenantId? tenant = null, long openingFloat = 10_000)
    {
        var assignment = new RegisterAssignment(
            tenant ?? Tenant, PointOfSaleId.New(), RegisterId.New(), cashier ?? Cashier);
        var session = CashRegisterSession.Open(SessionId.New(), assignment, Money.Fcfa(openingFloat), Morning);
        Sessions.Put(session);
        return session;
    }

    /// <summary>Vente validée directement par le domaine, déjà présente dans le dépôt.</summary>
    public Sale GivenRecordedSale(CashRegisterSession session)
    {
        var draft = new SaleDraft(
            SaleId.New(),
            1,
            Morning.AddMinutes(5),
            [SaleLine.Create(ProductId.New(), "Produit", 1, Money.Fcfa(1_000))],
            Money.Zero,
            [Payment.Cash(PaymentId.New(), Money.Fcfa(1_000), Money.Fcfa(1_000))],
            null);
        var sale = session.RecordSale(draft);
        Sales.Put(sale);
        return sale;
    }

    public RecordSaleCommand CashSale(
        CashRegisterSession session,
        long price = 1_000,
        long sequence = 1,
        long discount = 0,
        long? tendered = null,
        UserId? approver = null) =>
        new(
            SaleId.New(),
            session.Id,
            sequence,
            Morning.AddMinutes(10),
            [new SaleLineInput(ProductId.New(), "Produit", 1, price)],
            discount,
            [new PaymentInput(PaymentId.New(), PaymentMethod.Cash, price - discount, tendered ?? price - discount, null)],
            null,
            approver);

    public RecordSaleCommand MobileSale(
        CashRegisterSession session,
        string reference,
        PaymentMethod method = PaymentMethod.OrangeMoney,
        long price = 1_000,
        long sequence = 1) =>
        new(
            SaleId.New(),
            session.Id,
            sequence,
            Morning.AddMinutes(10),
            [new SaleLineInput(ProductId.New(), "Produit", 1, price)],
            0,
            [new PaymentInput(PaymentId.New(), method, price, null, reference)],
            null,
            null);
}
