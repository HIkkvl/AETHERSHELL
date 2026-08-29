using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherShell.Server.Migrations.Club
{
    /// <inheritdoc />
    public partial class ComputerCurrentTariffName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentTariffName",
                table: "Computers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentTariffName",
                table: "Computers");
        }
    }
}
