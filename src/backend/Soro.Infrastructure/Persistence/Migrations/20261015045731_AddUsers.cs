using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pin_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.CheckConstraint("ck_users_attempts", "failed_attempts >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_tenant_id_identifier",
                table: "users",
                columns: new[] { "tenant_id", "identifier" },
                unique: true);

            // Même isolation que les tables de caisse : un commerce ne voit que ses utilisateurs, et sans commerce
            // posé (connexion pas encore faite) aucune ligne n'est visible. Un utilisateur ne se supprime jamais :
            // il se désactive, pour garder l'attribution des ventes passées.
            migrationBuilder.Sql("""
                ALTER TABLE users ENABLE ROW LEVEL SECURITY;
                ALTER TABLE users FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON users
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE TRIGGER users_no_delete BEFORE DELETE ON users
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER users_no_delete ON users;
                DROP POLICY tenant_isolation ON users;
                """);

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
