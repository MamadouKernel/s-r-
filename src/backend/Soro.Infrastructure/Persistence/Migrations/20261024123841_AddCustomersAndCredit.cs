using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomersAndCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    credit_ceiling = table.Column<long>(type: "bigint", nullable: false),
                    debt = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                    table.CheckConstraint("ck_customers_amounts", "credit_ceiling >= 0 AND debt >= 0");
                });

            migrationBuilder.CreateTable(
                name: "debt_repayments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reference_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_debt_repayments", x => x.id);
                    table.CheckConstraint("ck_debt_repayments_values", "amount > 0 AND method <> 'Credit' AND ((method = 'Cash') = (reference IS NULL))");
                    table.ForeignKey(
                        name: "FK_debt_repayments_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customers_tenant_id_name",
                table: "customers",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_customers_tenant_id_phone",
                table: "customers",
                columns: new[] { "tenant_id", "phone" },
                unique: true,
                filter: "phone IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_debt_repayments_customer_id",
                table: "debt_repayments",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_debt_repayments_tenant_id_customer_id_at",
                table: "debt_repayments",
                columns: new[] { "tenant_id", "customer_id", "at" });

            migrationBuilder.CreateIndex(
                name: "IX_debt_repayments_tenant_id_method_reference_key",
                table: "debt_repayments",
                columns: new[] { "tenant_id", "method", "reference_key" },
                unique: true,
                filter: "reference_key IS NOT NULL");

            // Isolation par commerce comme les autres tables (RG-15). Un client ne se supprime jamais (il se désactive) et un versement est une écriture
            // figée : l'historique de la dette doit rester vérifiable (ADR 0008).
            migrationBuilder.Sql("""
                DO $$
                DECLARE table_name text;
                BEGIN
                    FOREACH table_name IN ARRAY ARRAY['customers', 'debt_repayments'] LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', table_name);
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON %I
                               USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)
                               WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)',
                            table_name);
                    END LOOP;
                END $$;
                CREATE TRIGGER customers_no_delete BEFORE DELETE ON customers
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                CREATE TRIGGER debt_repayments_immutable BEFORE UPDATE OR DELETE ON debt_repayments
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER debt_repayments_immutable ON debt_repayments;
                DROP TRIGGER customers_no_delete ON customers;
                """);

            migrationBuilder.DropTable(
                name: "debt_repayments");

            migrationBuilder.DropTable(
                name: "customers");
        }
    }
}
