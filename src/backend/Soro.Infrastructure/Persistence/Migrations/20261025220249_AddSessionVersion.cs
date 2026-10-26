using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "cash_register_sessions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "cash_register_sessions");
        }
    }
}
