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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureSessions(modelBuilder.Entity<SessionRow>());
        ConfigureSales(modelBuilder.Entity<SaleRow>());
        ConfigureSaleLines(modelBuilder.Entity<SaleLineRow>());
        ConfigurePayments(modelBuilder.Entity<PaymentRow>());
        ConfigureAudit(modelBuilder.Entity<AuditRow>());
        ConfigureUsers(modelBuilder.Entity<UserRow>());
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
            table.HasCheckConstraint("ck_sale_lines_values", "quantity > 0 AND unit_price >= 0"));
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
        user.ToTable("users", table => table.HasCheckConstraint("ck_users_attempts", "failed_attempts >= 0"));
        user.HasKey(row => row.Id);
        user.Property(row => row.Identifier).HasMaxLength(32);
        user.Property(row => row.DisplayName).HasMaxLength(100);
        user.Property(row => row.PinHash).HasMaxLength(200);
        user.Property(row => row.Role).HasConversion<string>().HasMaxLength(16);
        user.Property(row => row.Status).HasConversion<string>().HasMaxLength(16);

        // Un identifiant de connexion est unique dans un commerce, jamais entre commerces.
        user.HasIndex(row => new { row.TenantId, row.Identifier }).IsUnique();
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
