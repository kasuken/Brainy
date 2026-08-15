using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArchivedReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArchivedReason",
                table: "Task",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedReason",
                table: "Resource",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedReason",
                table: "Project",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedReason",
                table: "Output",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedReason",
                table: "Note",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedReason",
                table: "Idea",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedReason",
                table: "Goal",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedReason",
                table: "Area",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedReason",
                table: "Task");

            migrationBuilder.DropColumn(
                name: "ArchivedReason",
                table: "Resource");

            migrationBuilder.DropColumn(
                name: "ArchivedReason",
                table: "Project");

            migrationBuilder.DropColumn(
                name: "ArchivedReason",
                table: "Output");

            migrationBuilder.DropColumn(
                name: "ArchivedReason",
                table: "Note");

            migrationBuilder.DropColumn(
                name: "ArchivedReason",
                table: "Idea");

            migrationBuilder.DropColumn(
                name: "ArchivedReason",
                table: "Goal");

            migrationBuilder.DropColumn(
                name: "ArchivedReason",
                table: "Area");
        }
    }
}
