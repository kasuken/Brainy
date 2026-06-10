using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteFavorite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "Note",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Note_UserId_IsFavorite",
                table: "Note",
                columns: new[] { "UserId", "IsFavorite" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Note_UserId_IsFavorite",
                table: "Note");

            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "Note");
        }
    }
}
