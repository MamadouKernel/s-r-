using Soro.Application.Cash;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Application.Reporting;
using Soro.Application.Signup;
using Soro.Application.Stock;
using Soro.Domain.Stock;
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

    public FakeReportingQueries Reporting { get; } = new();

    public FakeAuditLogQueries AuditLog { get; } = new();

    public CapturingEmailSender Mail { get; } = new();

    public SequentialCodes Codes { get; } = new();

    public InMemoryEmailVerificationRepository Verifications { get; } = new();

    public InMemoryOwnerDirectory OwnerDirectory { get; } = new();

    public InMemoryCommerceRepository Commerces { get; } = new();

    public FakePasswordHasher Passwords { get; } = new();

    private FakeSignupSettings SignupSettings { get; } = new();

    public InMemoryProductRepository Products { get; } = new();

    public InMemoryStockMovementRepository Movements { get; } = new();

    public InMemoryUserRepository Users { get; } = new();

    public FakePinHasher Hasher { get; } = new();

    public FakeTenantScope Scope { get; } = new();

    private FakeTokenIssuer Tokens { get; } = new();

    private FakeShopSettings Settings { get; } = new();

    public OpenRegisterSessionHandler OpenSession => new(Current, Sessions, UnitOfWork);

    public CloseRegisterSessionHandler CloseSession => new(Current, Sessions, UnitOfWork);

    public RecordSaleHandler RecordSale => new(Current, Sessions, Sales, Products, Movements, Settings, Directory, Audit, Clock, UnitOfWork);

    public SyncSalesHandler SyncSales => new(RecordSale);

    public CancelSaleHandler CancelSale => new(Current, Sales, Audit, Clock, UnitOfWork);

    public StartSignupHandler StartSignup => new(OwnerDirectory, Verifications, Codes, Mail, SignupSettings, Clock, UnitOfWork);

    public ResendSignupCodeHandler ResendSignupCode => new(Verifications, Codes, Mail, Clock, UnitOfWork);

    public CompleteSignupHandler CompleteSignup =>
        new(Scope, Verifications, Codes, OwnerDirectory, Commerces, Users, Passwords, Audit, Tokens, SignupSettings, Clock, UnitOfWork);

    public OwnerLogInHandler OwnerLogIn => new(Scope, OwnerDirectory, Commerces, Users, Passwords, Tokens, Clock, UnitOfWork);

    public StartPasswordResetHandler StartPasswordReset => new(OwnerDirectory, Verifications, Codes, Mail, Clock, UnitOfWork);

    public ConfirmPasswordResetHandler ConfirmPasswordReset =>
        new(Scope, OwnerDirectory, Verifications, Codes, Users, Passwords, Audit, Clock, UnitOfWork);

    public LogInHandler LogIn => new(Scope, Users, Hasher, Tokens, Clock, UnitOfWork);

    public RegisterUserHandler RegisterUser => new(Current, Users, Hasher, Clock, UnitOfWork);

    public ListUsersHandler ListUsers => new(Current, Users, Clock);

    public GetDirectoryHandler GetDirectory => new(Current, Users);

    public DisableUserHandler DisableUser => new(Current, Users, Audit, Clock, UnitOfWork);

    public CreateProductHandler CreateProduct => new(Current, Products, Audit, Clock, UnitOfWork);

    public UpdateProductHandler UpdateProduct => new(Current, Products, Audit, Clock, UnitOfWork);

    public DeactivateProductHandler DeactivateProduct => new(Current, Products, Audit, Clock, UnitOfWork);

    public ReceiveStockHandler ReceiveStock => new(Current, Products, Movements, Clock, UnitOfWork);

    public RecordStockLossHandler RecordStockLoss => new(Current, Products, Movements, Audit, Clock, UnitOfWork);

    public ListProductsHandler ListProducts => new(Current, Products);

    public ListLowStockHandler ListLowStock => new(Current, Products);

    public ListStockMovementsHandler ListStockMovements => new(Current, Products, Movements);

    public ListSalesHandler ListSales => new(Current, Reporting);

    public GetSaleHandler GetSale => new(Current, Reporting);

    public ListSessionsHandler ListSessions => new(Current, Reporting);

    public GetActivityReportHandler ActivityReport => new(Current, Reporting);

    public GetTopProductsHandler TopProducts => new(Current, Reporting);

    public ListAuditLogHandler ListAuditLog => new(Current, AuditLog);

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

    public Product GivenProduct(
        string name = "Jus d'orange 1L",
        string? barcode = "6111000000011",
        int threshold = 5,
        int stock = 0,
        bool tracksStock = true,
        TenantId? tenant = null)
    {
        var product = Product.Create(
            ProductId.New(), tenant ?? Tenant, name, barcode, "Boissons",
            Money.Fcfa(400), Money.Fcfa(600), threshold, tracksStock, Morning);
        if (stock > 0)
        {
            product.Receive(stock, null, Manager, Morning);
        }

        Products.Put(product);
        return product;
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
