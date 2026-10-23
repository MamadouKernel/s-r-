using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "pin_hash",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "commerces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    activity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    owner_phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    formula = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    trial_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terms_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    terms_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commerces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "owner_directory",
                columns: table => new
                {
                    phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_owner_directory", x => x.phone);
                });

            migrationBuilder.CreateTable(
                name: "phone_verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    resend_count = table.Column<int>(type: "integer", nullable: false),
                    last_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_phone_verifications", x => x.id);
                    table.CheckConstraint("ck_phone_verifications_counters", "attempts >= 0 AND resend_count >= 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_credential",
                table: "users",
                sql: "pin_hash IS NOT NULL OR password_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_phone_verifications_phone_purpose_created_at",
                table: "phone_verifications",
                columns: new[] { "phone", "purpose", "created_at" });

            // Un commerce ne voit que sa propre fiche, comme les autres tables du commerce (RG-15), et ne se supprime jamais.
            migrationBuilder.Sql("""
                ALTER TABLE commerces ENABLE ROW LEVEL SECURITY;
                ALTER TABLE commerces FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON commerces
                    USING (id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE TRIGGER commerces_no_delete BEFORE DELETE ON commerces
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                """);

            // L'annuaire des propriétaires et les demandes de vérification existent avant tout commerce connu : pas d'isolation par
            // commerce possible. L'annuaire ne contient aucun secret (numéro → commerce) et ne perd jamais une entrée ; les demandes
            // ne portent que des condensats de codes et se purgeront après expiration.
            migrationBuilder.Sql("""
                CREATE TRIGGER owner_directory_immutable BEFORE UPDATE OR DELETE ON owner_directory
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER owner_directory_immutable ON owner_directory;
                DROP TRIGGER commerces_no_delete ON commerces;
                DROP POLICY tenant_isolation ON commerces;
                """);

            migrationBuilder.DropTable(
                name: "commerces");

            migrationBuilder.DropTable(
                name: "owner_directory");

            migrationBuilder.DropTable(
                name: "phone_verifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_credential",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "pin_hash",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
