using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutputFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedDate",
                table: "Output",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AreaId",
                table: "Output",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Output",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GoalId",
                table: "Output",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Output",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedDate",
                table: "Output",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Output_AreaId",
                table: "Output",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Output_GoalId",
                table: "Output",
                column: "GoalId");

            migrationBuilder.AddForeignKey(
                name: "FK_Output_Area_AreaId",
                table: "Output",
                column: "AreaId",
                principalTable: "Area",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Output_Goal_GoalId",
                table: "Output",
                column: "GoalId",
                principalTable: "Goal",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Output_Area_AreaId",
                table: "Output");

            migrationBuilder.DropForeignKey(
                name: "FK_Output_Goal_GoalId",
                table: "Output");

            migrationBuilder.DropIndex(
                name: "IX_Output_AreaId",
                table: "Output");

            migrationBuilder.DropIndex(
                name: "IX_Output_GoalId",
                table: "Output");

            migrationBuilder.DropColumn(
                name: "ArchivedDate",
                table: "Output");

            migrationBuilder.DropColumn(
                name: "AreaId",
                table: "Output");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Output");

            migrationBuilder.DropColumn(
                name: "GoalId",
                table: "Output");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Output");

            migrationBuilder.DropColumn(
                name: "PublishedDate",
                table: "Output");
        }
    }
}
