using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sessions_valid_from",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "revoked_at",
                table: "device_invitations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sessions_valid_from",
                table: "users");

            migrationBuilder.DropColumn(
                name: "revoked_at",
                table: "device_invitations");
        }
    }
}
