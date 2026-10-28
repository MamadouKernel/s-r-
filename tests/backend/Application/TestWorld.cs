using Soro.Application.Cash;
using Soro.Application.Customers;
using Soro.Application.Devices;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Application.Reporting;
using Soro.Application.Export;
using Soro.Application.Signup;
using Soro.Application.Subscription;
using Soro.Application.Stock;
using Soro.Domain.Stock;
using Soro.Domain.Cash;
using Soro.Domain.Customers;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Domain.Identity;
using Soro.Domain.Tenancy;

namespace Soro.Application.Tests;

/// <summary>Un commerce de test : un caissier, un gérant, des dépôts en mémoire et les cas d'usage câblés.</summary>
internal sealed class TestWorld
{
    public static readonly DateTimeOffset Morning = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    public TestWorld(InMemorySubscriptions? sharedSubscriptions = null)
    {
        Subscriptions = sharedSubscriptions ?? new InMemorySubscriptions();
        Subscriptions.Register(Commerces);
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

    public InMemorySubscriptions Subscriptions { get; }

    public FakeSubscriptionSettings SubscriptionSettings { get; } = new();

    public FakePasswordHasher Passwords { get; } = new();

    private FakeSignupSettings SignupSettings { get; } = new();

    public InMemoryDeviceRepository Devices { get; } = new();

    public InMemoryDeviceInvitationRepository DeviceInvitations { get; } = new();

    public FakeDeviceSecrets DeviceSecrets { get; } = new();

    public FakeDevicePolicy DevicePolicy { get; } = new();

    public InMemoryCustomerRepository Customers { get; } = new();

    public InMemoryDebtRepaymentRepository Repayments { get; } = new();

    public InMemoryProductRepository Products { get; } = new();

    public InMemoryProductFamilyRepository Families { get; } = new();

    public InMemoryPhotoStore Photos { get; } = new();

    public FakeDataExportQueries ExportQueries { get; } = new();

    public FakeDataExportWriter ExportWriter { get; } = new();

    public FakeSheetReader SheetReader { get; } = new();

    public FakeTemplateWriter TemplateWriter { get; } = new();

    public InMemoryStockMovementRepository Movements { get; } = new();

    public InMemoryUserRepository Users { get; } = new();

    public FakePinHasher Hasher { get; } = new();

    public FakeApprovalSeal Seal { get; } = new();

    public GrantApprovalHandler GrantApproval => new(Current, Users, Hasher, Seal, Audit, Clock, UnitOfWork);

    public FakeTenantScope Scope { get; } = new();

    private FakeTokenIssuer Tokens { get; } = new();

    private FakeShopSettings Settings { get; } = new();

    public OpenRegisterSessionHandler OpenSession => new(Current, Sessions, UnitOfWork);

    public InMemoryCashMovementRepository CashMovements { get; } = new();

    public RecordCashMovementHandler RecordCashMovement => new(Current, Sessions, CashMovements, Users, Hasher, Seal, Settings, Audit, Clock, UnitOfWork);

    public CloseRegisterSessionHandler CloseSession => new(Current, Sessions, Settings, Audit, Clock, UnitOfWork);

    public RecordSaleHandler RecordSale => new(Current, Sessions, Sales, Products, Movements, Customers, Settings, Users, Hasher, Seal, Repayments, Audit, Clock, UnitOfWork);

    public SyncSalesHandler SyncSales => new(RecordSale);

    public InMemorySaleReturnRepository Returns { get; } = new();

    public SaleReversal Reversal => new(Current, Sales, Returns, Sessions, Products, Movements, Customers, Users, Hasher, Seal, Audit, Clock, UnitOfWork);

    public ReturnSaleHandler ReturnSale => new(Reversal);

    public CancelSaleHandler CancelSale => new(Reversal);

    public CreateCustomerHandler CreateCustomer => new(Current, Customers, Settings, Clock, UnitOfWork);

    public UpdateCustomerHandler UpdateCustomer => new(Current, Customers, UnitOfWork);

    public DeactivateCustomerHandler DeactivateCustomer => new(Current, Customers, UnitOfWork);

    public ListCustomersHandler ListCustomers => new(Current, Customers);

    public RecordRepaymentHandler RecordRepayment => new(Current, Customers, Repayments, Sales, Audit, Clock, UnitOfWork);

    public ListRepaymentsHandler ListRepayments => new(Current, Customers, Repayments);

    public StartSignupHandler StartSignup => new(OwnerDirectory, Verifications, Codes, Mail, SignupSettings, Clock, UnitOfWork);

    public ResendSignupCodeHandler ResendSignupCode => new(Verifications, Codes, Mail, Clock, UnitOfWork);

    public CompleteSignupHandler CompleteSignup =>
        new(Scope, Verifications, Codes, OwnerDirectory, Commerces, Users, Passwords, Audit, Tokens, SignupSettings, Clock, UnitOfWork);

    public OwnerLogInHandler OwnerLogIn => new(Scope, OwnerDirectory, Commerces, Users, Passwords, Tokens, Clock, UnitOfWork);

    public StartPasswordResetHandler StartPasswordReset => new(OwnerDirectory, Verifications, Codes, Mail, Clock, UnitOfWork);

    public ConfirmPasswordResetHandler ConfirmPasswordReset =>
        new(Scope, OwnerDirectory, Verifications, Codes, Users, Passwords, Audit, Clock, UnitOfWork);

    public LogInHandler LogIn => new(Scope, Users, Devices, DeviceSecrets, DevicePolicy, Hasher, Tokens, Clock, UnitOfWork);

    public CreateDeviceInvitationHandler CreateDeviceInvitation => new(Current, DeviceInvitations, DeviceSecrets, Audit, Clock, UnitOfWork);

    public EnrollDeviceHandler EnrollDevice => new(Scope, DeviceInvitations, Devices, Commerces, DeviceSecrets, Audit, Clock, UnitOfWork);

    public ListDevicesHandler ListDevices => new(Current, Devices);

    public RevokeDeviceHandler RevokeDevice => new(Current, Devices, Audit, Clock, UnitOfWork);

    public CheckDeviceAccessHandler CheckDeviceAccess => new(Devices, Clock, UnitOfWork);

    public RegisterUserHandler RegisterUser => new(Current, Users, Hasher, Clock, UnitOfWork);

    public ListUsersHandler ListUsers => new(Current, Users, Clock);

    public GetDirectoryHandler GetDirectory => new(Current, Users);

    public DisableUserHandler DisableUser => new(Current, Users, Audit, Clock, UnitOfWork);

    public ResetUserPinHandler ResetUserPin => new(Current, Users, Hasher, Audit, Clock, UnitOfWork);

    public EnableUserHandler EnableUser => new(Current, Users, Hasher, Audit, Clock, UnitOfWork);

    public ChangeUserRoleHandler ChangeUserRole => new(Current, Users, Audit, Clock, UnitOfWork);

    public CheckSessionHandler CheckSession => new(Users);

    public ListDeviceInvitationsHandler ListDeviceInvitations => new(Current, DeviceInvitations, Users, Clock);

    public RevokeDeviceInvitationHandler RevokeDeviceInvitation => new(Current, DeviceInvitations, Audit, Clock, UnitOfWork);

    public CreateProductHandler CreateProduct => new(Current, Products, Families, Audit, Clock, UnitOfWork);

    public UpdateProductHandler UpdateProduct => new(Current, Products, Families, Audit, Clock, UnitOfWork);

    public CreateFamilyHandler CreateFamily => new(Current, Families, Products, Audit, Clock, UnitOfWork);

    public AddFormatHandler AddFormat => new(Current, Families, Products, Audit, Clock, UnitOfWork);

    public UpdateFamilyHandler UpdateFamily => new(Current, Families, Products, Audit, Clock, UnitOfWork);

    public GroupIntoFamilyHandler GroupIntoFamily => new(Current, Families, Products, Audit, Clock, UnitOfWork);

    public SetPhotoHandler SetPhoto => new(Current, Families, Products, Photos, UnitOfWork);

    public RemovePhotoHandler RemovePhoto => new(Current, Families, Products, Photos, UnitOfWork);

    public GetPhotoHandler GetPhoto => new(Current, Photos);

    public GetSubscriptionHandler GetSubscription => new(Current, Commerces, Subscriptions, SubscriptionSettings, Clock);

    public DeclareSubscriptionPaymentHandler DeclareSubscriptionPayment => new(Current, Subscriptions, SubscriptionSettings, Audit, Clock, UnitOfWork);

    public GetSubscriptionStateHandler SubscriptionState => new(Commerces, Clock);

    public RecordOfflineLoginsHandler RecordOfflineLogins => new(Current, Directory, Audit, Clock, UnitOfWork);

    public RecordExpiredSyncHandler RecordExpiredSync => new(Current, Audit, Clock, UnitOfWork);

    public ValidateSubscriptionPaymentHandler ValidatePayment => new(Subscriptions, Mail, Audit, Clock, UnitOfWork);

    public RefuseSubscriptionPaymentHandler RefusePayment => new(Subscriptions, Mail, Audit, Clock, UnitOfWork);

    public ListPendingSubscriptionPaymentsHandler ListPendingPayments => new(Subscriptions);

    public InMemoryEveningReportLog EveningLog { get; } = new();

    public GetDayReportHandler DayReport => new(Current, Reporting, Settings, Users, Clock);

    public SendEveningReportsHandler SendEveningReports => new(Subscriptions, Reporting, Settings, Users, EveningLog, Mail, Clock, UnitOfWork);

    public SendSubscriptionRemindersHandler SendReminders => new(Subscriptions, Mail, SubscriptionSettings, Clock, UnitOfWork);

    public ExportDataHandler ExportData => new(Current, Commerces, ExportQueries, ExportWriter, AuditLog, Audit, Clock, UnitOfWork);

    public GetImportTemplateHandler ImportTemplate => new(Current, TemplateWriter);

    public PreviewProductImportHandler PreviewImport => new(Current, SheetReader, Families, Products);

    public ImportProductsHandler ImportProducts => new(Current, SheetReader, Families, Products, Movements, Audit, Clock, UnitOfWork);

    public CreateInternalBarcodeHandler CreateInternalBarcode => new(Current, Products, Audit, Clock, UnitOfWork);

    public DeactivateProductHandler DeactivateProduct => new(Current, Products, Audit, Clock, UnitOfWork);

    public ReceiveStockHandler ReceiveStock => new(Current, Products, Movements, Clock, UnitOfWork);

    public RecordStockLossHandler RecordStockLoss => new(Current, Products, Movements, Audit, Clock, UnitOfWork);

    public ListProductsHandler ListProducts => new(Current, Products, Families);

    public ListLowStockHandler ListLowStock => new(Current, Products, Families);

    public ListStockMovementsHandler ListStockMovements => new(Current, Products, Movements);

    public ListSalesHandler ListSales => new(Current, Reporting, Clock);

    public GetSaleHandler GetSale => new(Current, Reporting);

    public ListSessionsHandler ListSessions => new(Current, Reporting);

    public GetActivityReportHandler ActivityReport => new(Current, Reporting);

    public GetTopProductsHandler TopProducts => new(Current, Reporting);

    public ListAuditLogHandler ListAuditLog => new(Current, AuditLog);

    public void ActAs(UserId user, Role role) => Current.Become(user, role);

    /// <summary>Le commerce du monde de test, tel que l'inscription le crée.</summary>
    public async Task GivenCommerceAsync(TenantId? tenant = null, string name = "Supérette du Carrefour")
    {
        var id = tenant ?? Tenant;
        await Commerces.AddAsync(
            Commerce.Open(id, name, ActivityType.Supermarket, EmailAddress.Parse("mamadou@exemple.ci"), "2026-10-brouillon", Clock.UtcNow),
            CancellationToken.None);
    }

    /// <summary>Un client du commerce, avec sa dette déjà portée (comme si des ventes à crédit avaient eu lieu).</summary>
    public Customer GivenCustomer(string name = "Awa Koné", string? phone = "07 07 07 07 07", long ceiling = 20_000, long debt = 0, TenantId? tenant = null)
    {
        var customer = Customer.Create(CustomerId.New(), tenant ?? Tenant, name, phone, Money.Fcfa(ceiling), Clock.UtcNow);
        if (debt > 0)
        {
            customer.Charge(Money.Fcfa(debt));
        }

        Customers.Put(customer);
        return customer;
    }

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
        Families.Put(ProductFamily.Create(product.FamilyId, product.Tenant, product.Name, product.Category, Morning));
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

    /// <summary>Une vente de <paramref name="quantity"/> fois ce produit, payée en espèces dans cette session, déjà présente dans le dépôt.</summary>
    public Sale GivenSale(CashRegisterSession session, Product product, int quantity, long price)
    {
        var total = quantity * price;
        var sale = session.RecordSale(new SaleDraft(
            SaleId.New(),
            1,
            Morning.AddMinutes(5),
            [SaleLine.Create(product.Id, product.Name, quantity, Money.Fcfa(price))],
            Money.Zero,
            [Payment.Cash(PaymentId.New(), Money.Fcfa(total), Money.Fcfa(total))],
            null));
        Sales.Put(sale);
        Sessions.Put(session);
        return sale;
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
        Approval? approval = null) =>
        new(
            SaleId.New(),
            session.Id,
            sequence,
            Morning.AddMinutes(10),
            [new SaleLineInput(ProductId.New(), "Produit", 1, price)],
            discount,
            [new PaymentInput(PaymentId.New(), PaymentMethod.Cash, price - discount, tendered ?? price - discount, null)],
            null,
            approval);

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
