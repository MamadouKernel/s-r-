using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeasureUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "unit",
                table: "sale_lines",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Piece");

            migrationBuilder.AddColumn<string>(
                name: "unit",
                table: "products",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Piece");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "unit",
                table: "sale_lines");

            migrationBuilder.DropColumn(
                name: "unit",
                table: "products");
        }
    }
}
