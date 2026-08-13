using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskLifecycleAndUserTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Task_UserId",
                table: "Task");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "UserDashboardPreference",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<Guid>(
                name: "ArchiveOperationId",
                table: "Task",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecurrenceSourceTaskId",
                table: "Task",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchiveOperationId",
                table: "Project",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusBeforeArchive",
                table: "Project",
                type: "int",
                nullable: true);

            // Preserve the safest recoverable lifecycle state for existing archived
            // projects. Tasks share the operation only when their archive timestamp
            // matches the project's cascade timestamp; older manual archives remain
            // independent and will not be resurrected by project restore.
            migrationBuilder.Sql("""
                UPDATE [Project]
                SET [ArchiveOperationId] = [Id],
                    [StatusBeforeArchive] = CASE WHEN [CompletedDate] IS NOT NULL THEN 3 ELSE 0 END
                WHERE [IsArchived] = 1;

                UPDATE t
                SET t.[ArchiveOperationId] = p.[ArchiveOperationId]
                FROM [Task] AS t
                INNER JOIN [Project] AS p ON p.[Id] = t.[ProjectId]
                WHERE t.[IsArchived] = 1
                  AND p.[IsArchived] = 1
                  AND t.[ArchivedAtUtc] IS NOT NULL
                  AND p.[ArchivedAtUtc] IS NOT NULL
                  AND t.[ArchivedAtUtc] = p.[ArchivedAtUtc];

                WITH RankedCurrent AS
                (
                    SELECT [Id],
                           ROW_NUMBER() OVER (
                               PARTITION BY [UserId]
                               ORDER BY [UpdatedAtUtc] DESC, [Id]) AS [RowNumber]
                    FROM [Task]
                    WHERE [IsCurrentTask] = 1
                )
                UPDATE t
                SET t.[IsCurrentTask] = 0
                FROM [Task] AS t
                INNER JOIN RankedCurrent AS r ON r.[Id] = t.[Id]
                WHERE r.[RowNumber] > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Task_RecurrenceSourceTaskId",
                table: "Task",
                column: "RecurrenceSourceTaskId",
                unique: true,
                filter: "[RecurrenceSourceTaskId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Task_UserId",
                table: "Task",
                column: "UserId",
                unique: true,
                filter: "[IsCurrentTask] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Task_RecurrenceSourceTaskId",
                table: "Task");

            migrationBuilder.DropIndex(
                name: "IX_Task_UserId",
                table: "Task");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "UserDashboardPreference");

            migrationBuilder.DropColumn(
                name: "ArchiveOperationId",
                table: "Task");

            migrationBuilder.DropColumn(
                name: "RecurrenceSourceTaskId",
                table: "Task");

            migrationBuilder.DropColumn(
                name: "ArchiveOperationId",
                table: "Project");

            migrationBuilder.DropColumn(
                name: "StatusBeforeArchive",
                table: "Project");

            migrationBuilder.CreateIndex(
                name: "IX_Task_UserId",
                table: "Task",
                column: "UserId");
        }
    }
}
