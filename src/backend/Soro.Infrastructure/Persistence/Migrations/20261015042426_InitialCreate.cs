using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cash_register_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    point_of_sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_float = table.Column<long>(type: "bigint", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cash_received = table.Column<long>(type: "bigint", nullable: false),
                    counted_cash = table.Column<long>(type: "bigint", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cash_variance = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_register_sessions", x => x.id);
                    table.CheckConstraint("ck_sessions_amounts", "opening_float >= 0 AND cash_received >= 0");
                });

            migrationBuilder.CreateTable(
                name: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    point_of_sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_sequence = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    discount = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gross_total = table.Column<long>(type: "bigint", nullable: false),
                    net_total = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales", x => x.id);
                    table.CheckConstraint("ck_sales_cancellation", "(status = 'Cancelled') = (cancelled_by IS NOT NULL)");
                    table.CheckConstraint("ck_sales_totals", "net_total = gross_total - discount AND net_total > 0");
                    table.ForeignKey(
                        name: "FK_sales_cash_register_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "cash_register_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_lines",
                columns: table => new
                {
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_lines", x => new { x.sale_id, x.position });
                    table.CheckConstraint("ck_sale_lines_values", "quantity > 0 AND unit_price >= 0");
                    table.ForeignKey(
                        name: "FK_sale_lines_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    change = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reference_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_payments", x => x.id);
                    table.CheckConstraint("ck_payments_values", "amount > 0 AND change >= 0 AND ((method IN ('Cash', 'Credit')) = (reference IS NULL))");
                    table.ForeignKey(
                        name: "FK_sale_payments_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_tenant_id_at",
                table: "audit_entries",
                columns: new[] { "tenant_id", "at" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_register_sessions_tenant_id_register_id",
                table: "cash_register_sessions",
                columns: new[] { "tenant_id", "register_id" },
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_sale_payments_sale_id",
                table: "sale_payments",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "IX_sale_payments_tenant_id_method_reference_key",
                table: "sale_payments",
                columns: new[] { "tenant_id", "method", "reference_key" },
                unique: true,
                filter: "reference_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_session_id",
                table: "sales",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_tenant_id_register_id_local_sequence",
                table: "sales",
                columns: new[] { "tenant_id", "register_id", "local_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_tenant_id_session_id",
                table: "sales",
                columns: new[] { "tenant_id", "session_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "sale_lines");

            migrationBuilder.DropTable(
                name: "sale_payments");

            migrationBuilder.DropTable(
                name: "sales");

            migrationBuilder.DropTable(
                name: "cash_register_sessions");
        }
    }
}
