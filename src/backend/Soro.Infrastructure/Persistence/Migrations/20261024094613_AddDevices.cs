using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    suggested_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_invitations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    secret_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_devices_tenant_id_enrolled_at",
                table: "devices",
                columns: new[] { "tenant_id", "enrolled_at" });

            // Isolation par commerce comme les autres tables (RG-15). Un appareil et une invitation ne se suppriment jamais : la révocation
            // et la consommation gardent la trace de qui a eu accès (ADR 0006).
            migrationBuilder.Sql("""
                DO $$
                DECLARE table_name text;
                BEGIN
                    FOREACH table_name IN ARRAY ARRAY['devices', 'device_invitations'] LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', table_name);
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON %I
                               USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)
                               WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)',
                            table_name);
                    END LOOP;
                END $$;
                CREATE TRIGGER devices_no_delete BEFORE DELETE ON devices
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                CREATE TRIGGER device_invitations_no_delete BEFORE DELETE ON device_invitations
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER device_invitations_no_delete ON device_invitations;
                DROP TRIGGER devices_no_delete ON devices;
                """);

            migrationBuilder.DropTable(
                name: "device_invitations");

            migrationBuilder.DropTable(
                name: "devices");
        }
    }
}
