using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherShell.Server.Migrations.Platform
{
    /// <inheritdoc />
    public partial class ClubSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Clubs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Временные уникальные значения, чтобы индекс не упал на пустых строках.
            // Нормальные slug из названия проставит старт сервера.
            migrationBuilder.Sql("""
                UPDATE "Clubs"
                SET "Slug" = 'club-' || "Id"::text
                WHERE "Slug" IS NULL OR "Slug" = '';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Clubs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_Slug",
                table: "Clubs",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clubs_Slug",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Clubs");
        }
    }
}
