using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductEventsAndAnalyticsConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AnalyticsEnabled",
                table: "UserDashboardPreference",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "ProductEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    EventName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PropertiesJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductEvent_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvent_OccurredAtUtc",
                table: "ProductEvent",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvent_UserId",
                table: "ProductEvent",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvent_UserId_EventName_OccurredAtUtc",
                table: "ProductEvent",
                columns: new[] { "UserId", "EventName", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductEvent");

            migrationBuilder.DropColumn(
                name: "AnalyticsEnabled",
                table: "UserDashboardPreference");
        }
    }
}
