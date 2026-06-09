using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateProjectStatusValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename stored status strings to match the updated ProjectStatus enum members.
            migrationBuilder.Sql("UPDATE [Project] SET [Status] = 'NotStarted' WHERE [Status] = 'Planning'");
            migrationBuilder.Sql("UPDATE [Project] SET [Status] = 'Waiting'    WHERE [Status] = 'OnHold'");
            migrationBuilder.Sql("UPDATE [Project] SET [Status] = 'Archived'   WHERE [Status] = 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Project] SET [Status] = 'Planning'  WHERE [Status] = 'NotStarted'");
            migrationBuilder.Sql("UPDATE [Project] SET [Status] = 'OnHold'    WHERE [Status] = 'Waiting'");
            migrationBuilder.Sql("UPDATE [Project] SET [Status] = 'Cancelled' WHERE [Status] = 'Archived'");
        }
    }
}
