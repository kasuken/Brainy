using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyTaskSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeeklyTaskSelection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeekStartDate = table.Column<DateTime>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyTaskSelection", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeeklyTaskSelection_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeeklyTaskSelection_Task_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Task",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyTaskSelection_TaskId",
                table: "WeeklyTaskSelection",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyTaskSelection_UserId",
                table: "WeeklyTaskSelection",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyTaskSelection_UserId_TaskId",
                table: "WeeklyTaskSelection",
                columns: new[] { "UserId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyTaskSelection_UserId_WeekStartDate",
                table: "WeeklyTaskSelection",
                columns: new[] { "UserId", "WeekStartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyTaskSelection_UserId_WeekStartDate_TaskId",
                table: "WeeklyTaskSelection",
                columns: new[] { "UserId", "WeekStartDate", "TaskId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeeklyTaskSelection");
        }
    }
}
