using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NoteTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TitlePattern = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentScaffold = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultParaCategory = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteTemplate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoteTemplate_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutputTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TitlePattern = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContentScaffold = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultSourceSelection = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutputTemplate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutputTemplate_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProjectNamePattern = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DesiredOutcome = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DefaultPriority = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DefaultAreaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DefaultGoalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTemplate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectTemplate_Area_DefaultAreaId",
                        column: x => x.DefaultAreaId,
                        principalTable: "Area",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProjectTemplate_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectTemplate_Goal_DefaultGoalId",
                        column: x => x.DefaultGoalId,
                        principalTable: "Goal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProjectTemplateTask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Priority = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Complexity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    DueDateOffsetDays = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTemplateTask", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectTemplateTask_ProjectTemplate_ProjectTemplateId",
                        column: x => x.ProjectTemplateId,
                        principalTable: "ProjectTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoteTemplate_UserId",
                table: "NoteTemplate",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutputTemplate_UserId",
                table: "OutputTemplate",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTemplate_DefaultAreaId",
                table: "ProjectTemplate",
                column: "DefaultAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTemplate_DefaultGoalId",
                table: "ProjectTemplate",
                column: "DefaultGoalId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTemplate_UserId",
                table: "ProjectTemplate",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTemplateTask_ProjectTemplateId",
                table: "ProjectTemplateTask",
                column: "ProjectTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoteTemplate");

            migrationBuilder.DropTable(
                name: "OutputTemplate");

            migrationBuilder.DropTable(
                name: "ProjectTemplateTask");

            migrationBuilder.DropTable(
                name: "ProjectTemplate");
        }
    }
}
