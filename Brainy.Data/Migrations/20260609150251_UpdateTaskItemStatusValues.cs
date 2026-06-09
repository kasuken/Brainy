using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTaskItemStatusValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename Blocked → Waiting to match the updated lifecycle model
            migrationBuilder.Sql("UPDATE [Task] SET [Status] = 'Waiting' WHERE [Status] = 'Blocked'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Task] SET [Status] = 'Blocked' WHERE [Status] = 'Waiting'");
        }
    }
}
