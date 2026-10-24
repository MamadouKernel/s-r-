using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Soro.Infrastructure.Persistence;

/// <summary>
/// Contexte EF Core. Les contraintes qui protègent les règles métier (unicités, totaux, références Mobile Money)
/// sont déclarées ici ; l'isolation par commerce et l'interdiction de supprimer sont dans la migration SQL.
/// </summary>
public sealed class SoroDbContext(DbContextOptions<SoroDbContext> options) : DbContext(options)
{
    internal DbSet<SessionRow> Sessions => Set<SessionRow>();

    internal DbSet<SaleRow> Sales => Set<SaleRow>();

    internal DbSet<SaleLineRow> SaleLines => Set<SaleLineRow>();

    internal DbSet<PaymentRow> Payments => Set<PaymentRow>();

    internal DbSet<AuditRow> AuditEntries => Set<AuditRow>();

    internal DbSet<UserRow> Users => Set<UserRow>();

    internal DbSet<ProductRow> Products => Set<ProductRow>();

    internal DbSet<StockMovementRow> StockMovements => Set<StockMovementRow>();

    internal DbSet<CommerceRow> Commerces => Set<CommerceRow>();

    internal DbSet<OwnerDirectoryRow> OwnerDirectory => Set<OwnerDirectoryRow>();

    internal DbSet<EmailVerificationRow> EmailVerifications => Set<EmailVerificationRow>();

    internal DbSet<CustomerRow> Customers => Set<CustomerRow>();

    internal DbSet<DebtRepaymentRow> DebtRepayments => Set<DebtRepaymentRow>();

    internal DbSet<DeviceRow> Devices => Set<DeviceRow>();

    internal DbSet<DeviceInvitationRow> DeviceInvitations => Set<DeviceInvitationRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureSessions(modelBuilder.Entity<SessionRow>());
        ConfigureSales(modelBuilder.Entity<SaleRow>());
        ConfigureSaleLines(modelBuilder.Entity<SaleLineRow>());
        ConfigurePayments(modelBuilder.Entity<PaymentRow>());
        ConfigureAudit(modelBuilder.Entity<AuditRow>());
        ConfigureUsers(modelBuilder.Entity<UserRow>());
        ConfigureProducts(modelBuilder.Entity<ProductRow>());
        ConfigureStockMovements(modelBuilder.Entity<StockMovementRow>());
        ConfigureCommerces(modelBuilder.Entity<CommerceRow>());
        ConfigureOwnerDirectory(modelBuilder.Entity<OwnerDirectoryRow>());
        ConfigureEmailVerifications(modelBuilder.Entity<EmailVerificationRow>());
        ConfigureCustomers(modelBuilder.Entity<CustomerRow>());
        ConfigureDebtRepayments(modelBuilder.Entity<DebtRepaymentRow>());
        ConfigureDevices(modelBuilder.Entity<DeviceRow>());
        ConfigureDeviceInvitations(modelBuilder.Entity<DeviceInvitationRow>());
        SnakeCase.ApplyToColumns(modelBuilder);
    }

    private static void ConfigureSessions(EntityTypeBuilder<SessionRow> session)
    {
        session.ToTable("cash_register_sessions", table =>
            table.HasCheckConstraint("ck_sessions_amounts", "opening_float >= 0 AND cash_received >= 0"));
        session.HasKey(row => row.Id);
        session.Property(row => row.Status).HasConversion<string>().HasMaxLength(16);

        // RG-23 : une seule session ouverte par caisse, garanti aussi par la base.
        session.HasIndex(row => new { row.TenantId, row.RegisterId })
            .IsUnique()
            .HasFilter("status = 'Open'");
    }

    private static void ConfigureSales(EntityTypeBuilder<SaleRow> sale)
    {
        sale.ToTable("sales", table =>
        {
            table.HasCheckConstraint("ck_sales_totals", "net_total = gross_total - discount AND net_total > 0");
            table.HasCheckConstraint("ck_sales_cancellation", "(status = 'Cancelled') = (cancelled_by IS NOT NULL)");
        });
        sale.HasKey(row => row.Id);
        sale.Property(row => row.Status).HasConversion<string>().HasMaxLength(16);
        sale.Property(row => row.CancelReason).HasMaxLength(500);

        // Ordre par caisse (RG-12) : une séquence locale n'est utilisée qu'une fois par caisse.
        sale.HasIndex(row => new { row.TenantId, row.RegisterId, row.LocalSequence }).IsUnique();
        sale.HasIndex(row => new { row.TenantId, row.SessionId });

        sale.HasOne<SessionRow>().WithMany().HasForeignKey(row => row.SessionId).OnDelete(DeleteBehavior.Restrict);
        sale.HasMany(row => row.Lines).WithOne().HasForeignKey(line => line.SaleId).OnDelete(DeleteBehavior.Restrict);
        sale.HasMany(row => row.Payments).WithOne().HasForeignKey(payment => payment.SaleId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureSaleLines(EntityTypeBuilder<SaleLineRow> line)
    {
        line.ToTable("sale_lines", table =>
            table.HasCheckConstraint("ck_sale_lines_values", "quantity > 0 AND unit_price >= 0 AND (unit_cost IS NULL OR unit_cost >= 0)"));
        line.HasKey(row => new { row.SaleId, row.Position });
        line.Property(row => row.ProductName).HasMaxLength(200);
    }

    private static void ConfigurePayments(EntityTypeBuilder<PaymentRow> payment)
    {
        payment.ToTable("sale_payments", table =>
            table.HasCheckConstraint(
                "ck_payments_values",
                "amount > 0 AND change >= 0 AND ((method IN ('Cash', 'Credit')) = (reference IS NULL))"));
        payment.HasKey(row => row.Id);
        payment.Property(row => row.Method).HasConversion<string>().HasMaxLength(16);
        payment.Property(row => row.Reference).HasMaxLength(100);
        payment.Property(row => row.ReferenceKey).HasMaxLength(100);

        // RG-04 : une référence Mobile Money ne sert qu'une fois par commerce et par opérateur.
        payment.HasIndex(row => new { row.TenantId, row.Method, row.ReferenceKey })
            .IsUnique()
            .HasFilter("reference_key IS NOT NULL");
    }

    private static void ConfigureUsers(EntityTypeBuilder<UserRow> user)
    {
        user.ToTable("users", table =>
        {
            table.HasCheckConstraint("ck_users_attempts", "failed_attempts >= 0");
            // Un profil se connecte par PIN (équipe) ou par mot de passe (propriétaire), jamais sans rien.
            table.HasCheckConstraint("ck_users_credential", "pin_hash IS NOT NULL OR password_hash IS NOT NULL");
        });
        user.HasKey(row => row.Id);
        user.Property(row => row.Identifier).HasMaxLength(32);
        user.Property(row => row.DisplayName).HasMaxLength(100);
        user.Property(row => row.PinHash).HasMaxLength(200);
        user.Property(row => row.Email).HasMaxLength(254);
        user.Property(row => row.PasswordHash).HasMaxLength(200);
        user.Property(row => row.Role).HasConversion<string>().HasMaxLength(16);
        user.Property(row => row.Status).HasConversion<string>().HasMaxLength(16);

        // Un identifiant de connexion est unique dans un commerce, jamais entre commerces.
        user.HasIndex(row => new { row.TenantId, row.Identifier }).IsUnique();
    }

    private static void ConfigureCommerces(EntityTypeBuilder<CommerceRow> commerce)
    {
        commerce.ToTable("commerces");
        commerce.HasKey(row => row.Id);
        commerce.Property(row => row.Name).HasMaxLength(100);
        commerce.Property(row => row.Activity).HasConversion<string>().HasMaxLength(16);
        commerce.Property(row => row.OwnerEmail).HasMaxLength(254);
        commerce.Property(row => row.Formula).HasMaxLength(32);
        commerce.Property(row => row.TermsVersion).HasMaxLength(32);
    }

    private static void ConfigureOwnerDirectory(EntityTypeBuilder<OwnerDirectoryRow> directory)
    {
        directory.ToTable("owner_directory");
        directory.HasKey(row => row.Email);
        directory.Property(row => row.Email).HasMaxLength(254);
    }

    private static void ConfigureEmailVerifications(EntityTypeBuilder<EmailVerificationRow> verification)
    {
        verification.ToTable("email_verifications", table =>
            table.HasCheckConstraint("ck_email_verifications_counters", "attempts >= 0 AND resend_count >= 0"));
        verification.HasKey(row => row.Id);
        verification.Property(row => row.Purpose).HasConversion<string>().HasMaxLength(16);
        verification.Property(row => row.Email).HasMaxLength(254);
        verification.Property(row => row.CodeHash).HasMaxLength(100);
        verification.Property(row => row.Payload).HasMaxLength(1000);
        verification.HasIndex(row => new { row.Email, row.Purpose, row.CreatedAt });
    }

    private static void ConfigureCustomers(EntityTypeBuilder<CustomerRow> customer)
    {
        customer.ToTable("customers", table =>
            table.HasCheckConstraint("ck_customers_amounts", "credit_ceiling >= 0 AND debt >= 0"));
        customer.HasKey(row => row.Id);
        customer.Property(row => row.Name).HasMaxLength(100);
        customer.Property(row => row.Phone).HasMaxLength(16);

        // Deux ventes à crédit simultanées pour un même client : la seconde échoue (xmin) et est rejouée, aucune écriture de dette ne se perd (CRD-07).
        customer.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        // Un numéro ne sert qu'à un client du commerce (jamais entre commerces).
        customer.HasIndex(row => new { row.TenantId, row.Phone }).IsUnique().HasFilter("phone IS NOT NULL");
        customer.HasIndex(row => new { row.TenantId, row.Name });
    }

    private static void ConfigureDebtRepayments(EntityTypeBuilder<DebtRepaymentRow> repayment)
    {
        repayment.ToTable("debt_repayments", table =>
            table.HasCheckConstraint(
                "ck_debt_repayments_values",
                "amount > 0 AND method <> 'Credit' AND ((method = 'Cash') = (reference IS NULL))"));
        repayment.HasKey(row => row.Id);
        repayment.Property(row => row.Method).HasConversion<string>().HasMaxLength(16);
        repayment.Property(row => row.Reference).HasMaxLength(100);
        repayment.Property(row => row.ReferenceKey).HasMaxLength(100);
        repayment.HasIndex(row => new { row.TenantId, row.CustomerId, row.At });

        // RG-04 appliquée aux versements : une référence Mobile Money ne sert qu'une fois par commerce et par opérateur.
        repayment.HasIndex(row => new { row.TenantId, row.Method, row.ReferenceKey })
            .IsUnique()
            .HasFilter("reference_key IS NOT NULL");
        repayment.HasOne<CustomerRow>().WithMany().HasForeignKey(row => row.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureDevices(EntityTypeBuilder<DeviceRow> device)
    {
        device.ToTable("devices");
        device.HasKey(row => row.Id);
        device.Property(row => row.Name).HasMaxLength(50);
        device.Property(row => row.SecretHash).HasMaxLength(100);
        device.HasIndex(row => new { row.TenantId, row.EnrolledAt });
    }

    private static void ConfigureDeviceInvitations(EntityTypeBuilder<DeviceInvitationRow> invitation)
    {
        invitation.ToTable("device_invitations");
        invitation.HasKey(row => row.Id);
        invitation.Property(row => row.SecretHash).HasMaxLength(100);
        invitation.Property(row => row.SuggestedName).HasMaxLength(50);

        // Deux appareils qui présentent le même lien en même temps : le second échoue (xmin) et son enrôlement est annulé en entier.
        invitation.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
    }

    private static void ConfigureProducts(EntityTypeBuilder<ProductRow> product)
    {
        product.ToTable("products", table =>
            table.HasCheckConstraint(
                "ck_products_values",
                "purchase_price >= 0 AND sale_price >= 0 AND alert_threshold >= 0 AND (tracks_stock OR quantity_on_hand = 0)"));
        product.HasKey(row => row.Id);
        product.Property(row => row.Name).HasMaxLength(200);
        product.Property(row => row.Barcode).HasMaxLength(64);
        product.Property(row => row.Category).HasMaxLength(100);

        // xmin, colonne système de PostgreSQL, sert de jeton de concurrence : deux ventes simultanées
        // ne s'écrasent pas, la seconde échoue et sera rejouée.
        product.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        // CAT-01 : un code-barres est unique dans le commerce (jamais entre commerces).
        product.HasIndex(row => new { row.TenantId, row.Barcode }).IsUnique().HasFilter("barcode IS NOT NULL");
        product.HasIndex(row => new { row.TenantId, row.Name });
    }

    private static void ConfigureStockMovements(EntityTypeBuilder<StockMovementRow> movement)
    {
        movement.ToTable("stock_movements", table =>
            table.HasCheckConstraint("ck_stock_movements_values", "quantity_delta <> 0 AND (unit_cost IS NULL OR unit_cost >= 0)"));
        movement.HasKey(row => row.Id);
        movement.Property(row => row.Kind).HasConversion<string>().HasMaxLength(24);
        movement.Property(row => row.Reason).HasMaxLength(500);
        movement.HasIndex(row => new { row.TenantId, row.ProductId, row.At });
        movement.HasOne<ProductRow>().WithMany().HasForeignKey(row => row.ProductId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAudit(EntityTypeBuilder<AuditRow> audit)
    {
        audit.ToTable("audit_entries");
        audit.HasKey(row => row.Id);
        audit.Property(row => row.Action).HasMaxLength(100);
        audit.Property(row => row.Detail).HasMaxLength(1000);
        audit.HasIndex(row => new { row.TenantId, row.At });
    }
}
