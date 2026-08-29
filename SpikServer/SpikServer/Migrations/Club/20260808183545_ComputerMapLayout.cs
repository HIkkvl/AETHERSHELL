using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherShell.Server.Migrations.Club
{
    /// <inheritdoc />
    public partial class ComputerMapLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MapX",
                table: "Computers",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MapY",
                table: "Computers",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MapX",
                table: "Computers");

            migrationBuilder.DropColumn(
                name: "MapY",
                table: "Computers");
        }
    }
}
