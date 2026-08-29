using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherShell.Server.Migrations.Platform
{
    /// <inheritdoc />
    public partial class ClientDiscountOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF NOT EXISTS — на случай, если колонку уже добавили вручную.
            migrationBuilder.Sql(
                """
                ALTER TABLE "Clients"
                ADD COLUMN IF NOT EXISTS "DiscountOverride" integer NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Clients"
                DROP COLUMN IF EXISTS "DiscountOverride";
                """);
        }
    }
}
