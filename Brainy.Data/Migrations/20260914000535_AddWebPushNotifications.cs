using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWebPushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PushNotificationDeliveryLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushNotificationDeliveryLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushNotificationDeliveryLog_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PushNotificationPreference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DailyFocusNudgeEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    OverdueTaskEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    WeeklyReviewReminderEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    QuietHoursEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    QuietHoursStart = table.Column<TimeOnly>(type: "time", nullable: false, defaultValue: new TimeOnly(21, 0, 0)),
                    QuietHoursEnd = table.Column<TimeOnly>(type: "time", nullable: false, defaultValue: new TimeOnly(8, 0, 0)),
                    DailyFocusNudgeHour = table.Column<int>(type: "int", nullable: false, defaultValue: 8),
                    WeeklyReviewDayOfWeek = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    WeeklyReviewHour = table.Column<int>(type: "int", nullable: false, defaultValue: 17),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushNotificationPreference", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushNotificationPreference_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PushSubscription",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    EndpointHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    P256dh = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Auth = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    DeviceLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastSuccessAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscription", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushSubscription_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushNotificationDeliveryLog_UserId",
                table: "PushNotificationDeliveryLog",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PushNotificationDeliveryLog_UserId_Category_CreatedAtUtc",
                table: "PushNotificationDeliveryLog",
                columns: new[] { "UserId", "Category", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PushNotificationPreference_UserId",
                table: "PushNotificationPreference",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscription_EndpointHash",
                table: "PushSubscription",
                column: "EndpointHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscription_UserId",
                table: "PushSubscription",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushNotificationDeliveryLog");

            migrationBuilder.DropTable(
                name: "PushNotificationPreference");

            migrationBuilder.DropTable(
                name: "PushSubscription");
        }
    }
}
