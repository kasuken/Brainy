using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameProjectWaitingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ProjectStatus.Waiting was split into Blocked ("blocked by someone/something")
            // and Parked ("intentionally parked"). Existing rows default to Blocked.
            migrationBuilder.Sql("UPDATE [Project] SET [Status] = 'Blocked' WHERE [Status] = 'Waiting'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Project] SET [Status] = 'Waiting' WHERE [Status] IN ('Blocked', 'Parked')");
        }
    }
}
