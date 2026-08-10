using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateIdeaWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CommittedAtUtc",
                table: "Idea",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CommittedProjectId",
                table: "Idea",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Evidence",
                table: "Idea",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplacedCommitment",
                table: "Idea",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuitabilityReason",
                table: "Idea",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetUserAndProblem",
                table: "Idea",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationExperiment",
                table: "Idea",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Idea_CommittedProjectId",
                table: "Idea",
                column: "CommittedProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Idea_Project_CommittedProjectId",
                table: "Idea",
                column: "CommittedProjectId",
                principalTable: "Project",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Idea_Project_CommittedProjectId",
                table: "Idea");

            migrationBuilder.DropIndex(
                name: "IX_Idea_CommittedProjectId",
                table: "Idea");

            migrationBuilder.DropColumn(
                name: "CommittedAtUtc",
                table: "Idea");

            migrationBuilder.DropColumn(
                name: "CommittedProjectId",
                table: "Idea");

            migrationBuilder.DropColumn(
                name: "Evidence",
                table: "Idea");

            migrationBuilder.DropColumn(
                name: "ReplacedCommitment",
                table: "Idea");

            migrationBuilder.DropColumn(
                name: "SuitabilityReason",
                table: "Idea");

            migrationBuilder.DropColumn(
                name: "TargetUserAndProblem",
                table: "Idea");

            migrationBuilder.DropColumn(
                name: "ValidationExperiment",
                table: "Idea");
        }
    }
}
