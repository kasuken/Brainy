using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GoalId",
                table: "Project",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Goal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AchievedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AreaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Goal_Area_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Area",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Goal_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GoalMilestone",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalMilestone", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoalMilestone_Goal_GoalId",
                        column: x => x.GoalId,
                        principalTable: "Goal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Project_GoalId",
                table: "Project",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_Goal_AreaId",
                table: "Goal",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Goal_IsArchived",
                table: "Goal",
                column: "IsArchived");

            migrationBuilder.CreateIndex(
                name: "IX_Goal_UserId",
                table: "Goal",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Goal_UserId_Status",
                table: "Goal",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GoalMilestone_GoalId",
                table: "GoalMilestone",
                column: "GoalId");

            migrationBuilder.AddForeignKey(
                name: "FK_Project_Goal_GoalId",
                table: "Project",
                column: "GoalId",
                principalTable: "Goal",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Project_Goal_GoalId",
                table: "Project");

            migrationBuilder.DropTable(
                name: "GoalMilestone");

            migrationBuilder.DropTable(
                name: "Goal");

            migrationBuilder.DropIndex(
                name: "IX_Project_GoalId",
                table: "Project");

            migrationBuilder.DropColumn(
                name: "GoalId",
                table: "Project");
        }
    }
}
