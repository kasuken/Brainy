using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixOfflineCaptureSyncRecordIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OfflineCaptureSyncRecord_IdempotencyKey",
                table: "OfflineCaptureSyncRecord");

            migrationBuilder.DropIndex(
                name: "IX_OfflineCaptureSyncRecord_UserId",
                table: "OfflineCaptureSyncRecord");

            migrationBuilder.CreateIndex(
                name: "IX_OfflineCaptureSyncRecord_UserId_IdempotencyKey",
                table: "OfflineCaptureSyncRecord",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OfflineCaptureSyncRecord_UserId_IdempotencyKey",
                table: "OfflineCaptureSyncRecord");

            migrationBuilder.CreateIndex(
                name: "IX_OfflineCaptureSyncRecord_IdempotencyKey",
                table: "OfflineCaptureSyncRecord",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfflineCaptureSyncRecord_UserId",
                table: "OfflineCaptureSyncRecord",
                column: "UserId");
        }
    }
}
