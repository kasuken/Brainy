using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStarterModeAndOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OnboardingCompleted",
                table: "UserDashboardPreference",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OnboardingDismissed",
                table: "UserDashboardPreference",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OnboardingStep",
                table: "UserDashboardPreference",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "StarterModeEnabled",
                table: "UserDashboardPreference",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnboardingCompleted",
                table: "UserDashboardPreference");

            migrationBuilder.DropColumn(
                name: "OnboardingDismissed",
                table: "UserDashboardPreference");

            migrationBuilder.DropColumn(
                name: "OnboardingStep",
                table: "UserDashboardPreference");

            migrationBuilder.DropColumn(
                name: "StarterModeEnabled",
                table: "UserDashboardPreference");
        }
    }
}
