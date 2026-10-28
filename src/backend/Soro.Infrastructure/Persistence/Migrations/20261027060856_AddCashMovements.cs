using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "cash_paid_in",
                table: "cash_register_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "cash_paid_out",
                table: "cash_register_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "count_detail",
                table: "cash_register_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "first_count",
                table: "cash_register_sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_count_detail",
                table: "cash_register_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cash_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_movements", x => x.id);
                    table.CheckConstraint("ck_cash_movements_amount", "amount > 0");
                    table.ForeignKey(
                        name: "FK_cash_movements_cash_register_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "cash_register_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_session_id",
                table: "cash_movements",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_tenant_id_at",
                table: "cash_movements",
                columns: new[] { "tenant_id", "at" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_tenant_id_session_id",
                table: "cash_movements",
                columns: new[] { "tenant_id", "session_id" });

            // Isolation par commerce comme les autres tables (RG-15) ; un mouvement ne se modifie ni ne se supprime : une erreur se corrige par un
            // mouvement inverse motivé (docs/decisions/0021).
            migrationBuilder.Sql("""
                DO $$
                DECLARE table_name text;
                BEGIN
                    FOREACH table_name IN ARRAY ARRAY['cash_movements'] LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', table_name);
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON %I
                               USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)
                               WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)',
                            table_name);
                        EXECUTE format(
                            'CREATE TRIGGER %I BEFORE UPDATE OR DELETE ON %I FOR EACH ROW EXECUTE FUNCTION forbid_change()',
                            table_name || '_immutable', table_name);
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER cash_movements_immutable ON cash_movements;");

            migrationBuilder.DropTable(
                name: "cash_movements");

            migrationBuilder.DropColumn(
                name: "cash_paid_in",
                table: "cash_register_sessions");

            migrationBuilder.DropColumn(
                name: "cash_paid_out",
                table: "cash_register_sessions");

            migrationBuilder.DropColumn(
                name: "count_detail",
                table: "cash_register_sessions");

            migrationBuilder.DropColumn(
                name: "first_count",
                table: "cash_register_sessions");

            migrationBuilder.DropColumn(
                name: "first_count_detail",
                table: "cash_register_sessions");
        }
    }
}
