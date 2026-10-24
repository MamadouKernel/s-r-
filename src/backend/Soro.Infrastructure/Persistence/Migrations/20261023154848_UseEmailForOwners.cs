using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UseEmailForOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // L'identifiant du propriétaire passe du téléphone à l'adresse e-mail : un numéro ne se convertit pas en adresse.
            // Aucun commerce n'existe en production à ce stade (l'inscription vient d'être livrée) ; en développement, on refuse
            // plutôt que de perdre des comptes sans prévenir : supprimer la base de développement (scripts/dev.sh reset) puis rejouer.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM owner_directory) OR EXISTS (SELECT 1 FROM commerces) THEN
                        RAISE EXCEPTION 'Des comptes propriétaires identifiés par téléphone existent : leur identifiant ne peut pas être converti en adresse e-mail. Repartez d''une base vide (scripts/dev.sh reset).';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "phone_verifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_owner_directory",
                table: "owner_directory");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "users");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "owner_directory");

            migrationBuilder.DropColumn(
                name: "owner_phone",
                table: "commerces");

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "owner_directory",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "owner_email",
                table: "commerces",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_owner_directory",
                table: "owner_directory",
                column: "email");

            migrationBuilder.CreateTable(
                name: "email_verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
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
                    table.PrimaryKey("PK_email_verifications", x => x.id);
                    table.CheckConstraint("ck_email_verifications_counters", "attempts >= 0 AND resend_count >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_verifications_email_purpose_created_at",
                table: "email_verifications",
                columns: new[] { "email", "purpose", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_verifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_owner_directory",
                table: "owner_directory");

            migrationBuilder.DropColumn(
                name: "email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email",
                table: "owner_directory");

            migrationBuilder.DropColumn(
                name: "owner_email",
                table: "commerces");

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "owner_directory",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "owner_phone",
                table: "commerces",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_owner_directory",
                table: "owner_directory",
                column: "phone");

            migrationBuilder.CreateTable(
                name: "phone_verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    resend_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_phone_verifications", x => x.id);
                    table.CheckConstraint("ck_phone_verifications_counters", "attempts >= 0 AND resend_count >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_phone_verifications_phone_purpose_created_at",
                table: "phone_verifications",
                columns: new[] { "phone", "purpose", "created_at" });
        }
    }
}
