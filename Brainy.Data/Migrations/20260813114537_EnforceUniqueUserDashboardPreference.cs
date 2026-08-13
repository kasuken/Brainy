using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueUserDashboardPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ;WITH RankedPreferences AS
                (
                    SELECT [Id],
                           ROW_NUMBER() OVER
                           (
                               PARTITION BY [UserId]
                               ORDER BY [UpdatedAtUtc] DESC, [CreatedAtUtc] DESC, [Id] DESC
                           ) AS [PreferenceRank]
                    FROM [UserDashboardPreference]
                )
                DELETE FROM RankedPreferences
                WHERE [PreferenceRank] > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_UserDashboardPreference_UserId",
                table: "UserDashboardPreference");

            migrationBuilder.CreateIndex(
                name: "IX_UserDashboardPreference_UserId",
                table: "UserDashboardPreference",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserDashboardPreference_UserId",
                table: "UserDashboardPreference");

            migrationBuilder.CreateIndex(
                name: "IX_UserDashboardPreference_UserId",
                table: "UserDashboardPreference",
                column: "UserId");
        }
    }
}
