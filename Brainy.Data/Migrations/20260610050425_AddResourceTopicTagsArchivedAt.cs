using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceTopicTagsArchivedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAtUtc",
                table: "Resource",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Topic",
                table: "Resource",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ResourceTag",
                columns: table => new
                {
                    ResourcesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceTag", x => new { x.ResourcesId, x.TagsId });
                    table.ForeignKey(
                        name: "FK_ResourceTag_Resource_ResourcesId",
                        column: x => x.ResourcesId,
                        principalTable: "Resource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ResourceTag_Tag_TagsId",
                        column: x => x.TagsId,
                        principalTable: "Tag",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Resource_Topic",
                table: "Resource",
                column: "Topic");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceTag_TagsId",
                table: "ResourceTag",
                column: "TagsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceTag");

            migrationBuilder.DropIndex(
                name: "IX_Resource_Topic",
                table: "Resource");

            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                table: "Resource");

            migrationBuilder.DropColumn(
                name: "Topic",
                table: "Resource");
        }
    }
}
