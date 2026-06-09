using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTaskPriorityValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename Urgent → Critical to align with the task priority model
            migrationBuilder.Sql("UPDATE [Task] SET [Priority] = 'Critical' WHERE [Priority] = 'Urgent'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Task] SET [Priority] = 'Urgent' WHERE [Priority] = 'Critical'");
        }
    }
}
