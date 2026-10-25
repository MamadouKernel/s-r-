using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductFamiliesAndPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "family_id",
                table: "products",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "format_label",
                table: "products",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "photo_id",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_photos", x => x.id);
                    table.CheckConstraint("ck_photos_size", "removed_at IS NOT NULL OR octet_length(data) BETWEEN 1 AND 204800");
                });

            migrationBuilder.CreateTable(
                name: "product_families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    photo_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_families", x => x.id);
                    table.ForeignKey(
                        name: "FK_product_families_photos_photo_id",
                        column: x => x.photo_id,
                        principalTable: "photos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_products_family_id",
                table: "products",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_photo_id",
                table: "products",
                column: "photo_id");

            migrationBuilder.CreateIndex(
                name: "IX_photos_tenant_id",
                table: "photos",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_families_photo_id",
                table: "product_families",
                column: "photo_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_families_tenant_id_name",
                table: "product_families",
                columns: new[] { "tenant_id", "name" });

            // Reprise de l'existant (docs/specs/formats-produits FMT-08) : chaque produit devient sa propre famille, au même identifiant,
            // sans libellé. Aucun nom, prix, stock ni vente ne change. « row_security = off » échoue bruyamment si le rôle n'a pas le droit de
            // contourner l'isolation par commerce, plutôt que de ne rien copier en silence.
            migrationBuilder.Sql("""
                SET LOCAL row_security = off;
                INSERT INTO product_families (id, tenant_id, name, category, created_at)
                    SELECT id, tenant_id, name, category, created_at FROM products;
                UPDATE products SET family_id = id;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_products_photos_photo_id",
                table: "products",
                column: "photo_id",
                principalTable: "photos",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_products_product_families_family_id",
                table: "products",
                column: "family_id",
                principalTable: "product_families",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Un libellé de format est unique dans sa famille, sans tenir compte de la casse (les espaces sont déjà normalisés par le domaine).
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_products_family_label ON products (family_id, upper(format_label)) WHERE format_label IS NOT NULL;
                """);

            // Isolation par commerce comme les autres tables (RG-15) : une famille et une photo ne se voient que dans leur commerce.
            migrationBuilder.Sql("""
                DO $$
                DECLARE table_name text;
                BEGIN
                    FOREACH table_name IN ARRAY ARRAY['product_families', 'photos'] LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', table_name);
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON %I
                               USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)
                               WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)',
                            table_name);
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX ux_products_family_label;");

            migrationBuilder.DropForeignKey(
                name: "FK_products_photos_photo_id",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "FK_products_product_families_family_id",
                table: "products");

            migrationBuilder.DropTable(
                name: "product_families");

            migrationBuilder.DropTable(
                name: "photos");

            migrationBuilder.DropIndex(
                name: "IX_products_family_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_photo_id",
                table: "products");

            migrationBuilder.DropColumn(
                name: "family_id",
                table: "products");

            migrationBuilder.DropColumn(
                name: "format_label",
                table: "products");

            migrationBuilder.DropColumn(
                name: "photo_id",
                table: "products");
        }
    }
}
