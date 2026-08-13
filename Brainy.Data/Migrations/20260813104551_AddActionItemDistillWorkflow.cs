using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActionItemDistillWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "ActionItem",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptVersion",
                table: "ActionItem",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "ActionItem",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            // Backfill ownership from the source note first, then from an already-promoted task.
            // Rows with neither relationship are intentionally retained with null ownership; the
            // tenant-scoped service never exposes them and an administrator can reconcile them later.
            migrationBuilder.Sql(
                """
                UPDATE actionItem
                SET actionItem.[UserId] = note.[UserId]
                FROM [ActionItem] AS actionItem
                INNER JOIN [Note] AS note ON note.[Id] = actionItem.[NoteId]
                WHERE actionItem.[UserId] IS NULL;

                UPDATE actionItem
                SET actionItem.[UserId] = taskItem.[UserId]
                FROM [ActionItem] AS actionItem
                INNER JOIN [Task] AS taskItem ON taskItem.[Id] = actionItem.[TaskItemId]
                WHERE actionItem.[UserId] IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ActionItem_UserId_NoteId",
                table: "ActionItem",
                columns: new[] { "UserId", "NoteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ActionItem_AspNetUsers_UserId",
                table: "ActionItem",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActionItem_AspNetUsers_UserId",
                table: "ActionItem");

            migrationBuilder.DropIndex(
                name: "IX_ActionItem_UserId_NoteId",
                table: "ActionItem");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "ActionItem");

            migrationBuilder.DropColumn(
                name: "PromptVersion",
                table: "ActionItem");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ActionItem");
        }
    }
}
