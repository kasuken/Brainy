using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteArchiveAndRetentionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAtUtc",
                table: "Note",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Note",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ArchiveRetentionRule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RetentionDays = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveRetentionRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchiveRetentionRule_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Note_UserId_IsArchived",
                table: "Note",
                columns: new[] { "UserId", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveRetentionRule_UserId",
                table: "ArchiveRetentionRule",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveRetentionRule_UserId_EntityType",
                table: "ArchiveRetentionRule",
                columns: new[] { "UserId", "EntityType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchiveRetentionRule");

            migrationBuilder.DropIndex(
                name: "IX_Note_UserId_IsArchived",
                table: "Note");

            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                table: "Note");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Note");
        }
    }
}
