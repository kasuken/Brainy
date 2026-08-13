using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brainy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLifecycleActivityLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LifecycleActivity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Context = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Link = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifecycleActivity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LifecycleActivity_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LifecycleActivity_ActivityType",
                table: "LifecycleActivity",
                column: "ActivityType");

            migrationBuilder.CreateIndex(
                name: "IX_LifecycleActivity_UserId",
                table: "LifecycleActivity",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LifecycleActivity_UserId_OccurredAtUtc",
                table: "LifecycleActivity",
                columns: new[] { "UserId", "OccurredAtUtc" });

            // Preserve the lifecycle facts that the legacy mutable projections still retain.
            // Reopen/restore events cannot be reconstructed because those transitions cleared
            // their timestamps, so those event types begin accumulating after this migration.
            migrationBuilder.Sql(
                """
                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'NoteCaptured', [CreatedAtUtc], [Title], N'Captured',
                       N'/notes/' + CONVERT(nvarchar(36), [Id])
                FROM [Note];

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'NoteProcessed', [ProcessedAtUtc], [Title], N'Processed from Inbox',
                       N'/notes/' + CONVERT(nvarchar(36), [Id])
                FROM [Note]
                WHERE [ProcessedAtUtc] IS NOT NULL;

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'NoteArchived', [ArchivedAtUtc], [Title], N'Archived',
                       N'/notes/' + CONVERT(nvarchar(36), [Id])
                FROM [Note]
                WHERE [ArchivedAtUtc] IS NOT NULL;

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'TaskCreated', [CreatedAtUtc], [Title], N'Created',
                       N'/projects/' + CONVERT(nvarchar(36), [ProjectId])
                FROM [Task];

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'TaskCompleted', [CompletedDate], [Title], N'Completed',
                       N'/projects/' + CONVERT(nvarchar(36), [ProjectId])
                FROM [Task]
                WHERE [CompletedDate] IS NOT NULL;

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'TaskArchived', [ArchivedAtUtc], [Title], N'Archived',
                       N'/projects/' + CONVERT(nvarchar(36), [ProjectId])
                FROM [Task]
                WHERE [ArchivedAtUtc] IS NOT NULL;

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'ProjectCreated', [CreatedAtUtc], [Name], N'Created',
                       N'/projects/' + CONVERT(nvarchar(36), [Id])
                FROM [Project];

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'ProjectCompleted', [CompletedDate], [Name], N'Completed',
                       N'/projects/' + CONVERT(nvarchar(36), [Id])
                FROM [Project]
                WHERE [CompletedDate] IS NOT NULL;

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'ProjectArchived', [ArchivedAtUtc], [Name], N'Archived',
                       N'/projects/' + CONVERT(nvarchar(36), [Id])
                FROM [Project]
                WHERE [ArchivedAtUtc] IS NOT NULL;

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'OutputCreated', [CreatedAtUtc], [Title], N'Created',
                       N'/outputs/' + CONVERT(nvarchar(36), [Id])
                FROM [Output];

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'OutputPublished', [PublishedDate], [Title], N'Published',
                       N'/outputs/' + CONVERT(nvarchar(36), [Id])
                FROM [Output]
                WHERE [PublishedDate] IS NOT NULL;

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'OutputArchived', [ArchivedDate], [Title], N'Archived',
                       N'/outputs/' + CONVERT(nvarchar(36), [Id])
                FROM [Output]
                WHERE [ArchivedDate] IS NOT NULL;

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'IdeaCaptured', [CreatedAtUtc], [Title], N'Captured',
                       N'/ideas/' + CONVERT(nvarchar(36), [Id])
                FROM [Idea];

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'IdeaCommitted', [CommittedAtUtc], [Title], N'Committed',
                       N'/ideas/' + CONVERT(nvarchar(36), [Id])
                FROM [Idea]
                WHERE [CommittedAtUtc] IS NOT NULL;

                INSERT INTO [LifecycleActivity]
                    ([Id], [UserId], [EntityId], [ActivityType], [OccurredAtUtc], [Title], [Context], [Link])
                SELECT NEWID(), [UserId], [Id], N'GoalAchieved', [AchievedDate], [Title], N'Achieved',
                       N'/goals/' + CONVERT(nvarchar(36), [Id])
                FROM [Goal]
                WHERE [AchievedDate] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LifecycleActivity");
        }
    }
}
