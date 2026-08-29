using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherShell.Server.Migrations.Club
{
    /// <inheritdoc />
    public partial class TariffBurnablePackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBurnable",
                table: "Tariffs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SessionSavesRemaining",
                table: "Computers",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBurnable",
                table: "Tariffs");

            migrationBuilder.DropColumn(
                name: "SessionSavesRemaining",
                table: "Computers");
        }
    }
}
