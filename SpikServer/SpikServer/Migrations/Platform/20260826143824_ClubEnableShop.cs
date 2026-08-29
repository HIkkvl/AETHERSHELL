using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherShell.Server.Migrations.Platform
{
    /// <inheritdoc />
    public partial class ClubEnableShop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableShop",
                table: "Clubs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableShop",
                table: "Clubs");
        }
    }
}
