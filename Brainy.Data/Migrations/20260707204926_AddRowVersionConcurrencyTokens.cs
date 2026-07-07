using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRowVersionConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "UserDashboardPreference",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TaskDependency",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Task",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Tag",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Summary",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Source",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Resource",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Project",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Output",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "NoteRelationship",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "NoteImage",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Note",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Idea",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Highlight",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "GoalMilestone",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "GoalActivity",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Goal",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Area",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ArchiveRetentionRule",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ActionItem",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "UserDashboardPreference");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TaskDependency");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Task");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Tag");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Summary");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Source");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Resource");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Project");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Output");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "NoteRelationship");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "NoteImage");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Note");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Idea");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Highlight");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "GoalMilestone");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "GoalActivity");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Goal");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Area");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ArchiveRetentionRule");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ActionItem");
        }
    }
}
