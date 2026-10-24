using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_values",
                table: "stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_lines_values",
                table: "sale_lines");

            migrationBuilder.AddColumn<long>(
                name: "unit_cost",
                table: "stock_movements",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "unit_cost",
                table: "sale_lines",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_values",
                table: "stock_movements",
                sql: "quantity_delta <> 0 AND (unit_cost IS NULL OR unit_cost >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sale_lines_values",
                table: "sale_lines",
                sql: "quantity > 0 AND unit_price >= 0 AND (unit_cost IS NULL OR unit_cost >= 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_values",
                table: "stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_lines_values",
                table: "sale_lines");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                table: "sale_lines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_values",
                table: "stock_movements",
                sql: "quantity_delta <> 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sale_lines_values",
                table: "sale_lines",
                sql: "quantity > 0 AND unit_price >= 0");
        }
    }
}
