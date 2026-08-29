using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherShell.Server.Migrations.Platform
{
    /// <inheritdoc />
    public partial class ClientAvatarUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "Clients",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "Clients");
        }
    }
}
