using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paid_until",
                table: "commerces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subscription_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reference_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    declared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    covered_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    covered_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_payments", x => x.id);
                    table.CheckConstraint("ck_subscription_payments_amount", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "subscription_references",
                columns: table => new
                {
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reference_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_references", x => new { x.method, x.reference_key });
                });

            migrationBuilder.CreateTable(
                name: "subscription_reminders",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_reminders", x => new { x.tenant_id, x.ends_at, x.kind });
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_status",
                table: "subscription_payments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_tenant_id_declared_at",
                table: "subscription_payments",
                columns: new[] { "tenant_id", "declared_at" });

            // Isolation par commerce comme les autres tables (RG-15). Un paiement déclaré ne se supprime jamais : seule sa décision change.
            // La table des références n'a pas de commerce (c'est ce qui permet de refuser une référence déjà vue ailleurs) ; elle ne contient
            // rien d'autre qu'un moyen et une clé.
            migrationBuilder.Sql("""
                ALTER TABLE subscription_payments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE subscription_payments FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON subscription_payments
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE subscription_reminders ENABLE ROW LEVEL SECURITY;
                ALTER TABLE subscription_reminders FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON subscription_reminders
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE TRIGGER subscription_payments_no_delete BEFORE DELETE ON subscription_payments
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                CREATE TRIGGER subscription_references_immutable BEFORE UPDATE OR DELETE ON subscription_references
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER subscription_references_immutable ON subscription_references;
                DROP TRIGGER subscription_payments_no_delete ON subscription_payments;
                """);

            migrationBuilder.DropTable(
                name: "subscription_payments");

            migrationBuilder.DropTable(
                name: "subscription_references");

            migrationBuilder.DropTable(
                name: "subscription_reminders");

            migrationBuilder.DropColumn(
                name: "paid_until",
                table: "commerces");
        }
    }
}
