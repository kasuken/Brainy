using Bogus;
using Brainy.Data.Identity;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Brainy.Data;

internal static class DevelopmentDataSeeder
{
    private const string DemoUserEmail = "demo@brainy.local";
    private const string DemoUserPassword = "Brainy-dev-123!";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<BrainyDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = services.GetRequiredService<ILogger<BrainyDbContext>>();

        var user = await userManager.FindByEmailAsync(DemoUserEmail);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = DemoUserEmail,
                Email = DemoUserEmail,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, DemoUserPassword);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Could not create development demo user: {errors}");
            }
        }

        var hasDemoData = await db.Notes.AnyAsync(note => note.UserId == user.Id)
            || await db.Projects.AnyAsync(project => project.UserId == user.Id)
            || await db.Areas.AnyAsync(area => area.UserId == user.Id);

        if (hasDemoData)
        {
            logger.LogDebug("Development seed data already exists for {Email}; skipping.", DemoUserEmail);
            return;
        }

        var faker = new Faker("en");
        var now = DateTime.UtcNow;

        var areas = BuildAreas(user.Id, now);
        var resources = BuildResources(user.Id, areas, now);
        var projects = BuildProjects(user.Id, areas, faker, now);
        var tasks = BuildTasks(user.Id, projects, faker, now);
        var tags = BuildTags(user.Id, now);
        var sources = BuildSources(user.Id, faker, now);
        var notes = BuildNotes(user.Id, projects, areas, resources, sources, tags, faker, now);
        var outputs = BuildOutputs(user.Id, projects, notes, faker, now);
        var relationships = BuildRelationships(notes, now);

        db.Areas.AddRange(areas);
        db.Resources.AddRange(resources);
        db.Projects.AddRange(projects);
        db.Tasks.AddRange(tasks);
        db.Tags.AddRange(tags);
        db.Sources.AddRange(sources);
        db.Notes.AddRange(notes);
        db.Outputs.AddRange(outputs);
        db.NoteRelationships.AddRange(relationships);
        db.DashboardPreferences.Add(new UserDashboardPreference
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            WidgetOrder = "[\"CurrentTask\",\"Overdue\",\"DueToday\",\"ThisWeek\",\"NextTasks\",\"HighPriority\",\"InboxReminder\",\"FocusSummary\"]",
            CollapsedWidgets = "[]",
            InboxWarningThreshold = 8,
            CreatedAtUtc = now.AddDays(-14),
            UpdatedAtUtc = now.AddDays(-2)
        });

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seeded development database with realistic sample data for {Email}. Password: {Password}",
            DemoUserEmail,
            DemoUserPassword);
    }

    private static List<Area> BuildAreas(string userId, DateTime now)
    {
        return
        [
            new Area
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Product Leadership",
                Description = "Ongoing responsibilities around product strategy, prioritization, and team alignment.",
                Purpose = "Keep product direction clear and decisions easy to revisit.",
                CreatedAtUtc = now.AddDays(-160),
                UpdatedAtUtc = now.AddDays(-3)
            },
            new Area
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Personal Knowledge System",
                Description = "The routines, reviews, and habits that keep the second brain useful.",
                Purpose = "Turn captured material into reusable work instead of passive storage.",
                CreatedAtUtc = now.AddDays(-220),
                UpdatedAtUtc = now.AddDays(-1)
            },
            new Area
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Professional Development",
                Description = "Learning plans, feedback, coaching notes, and skill development.",
                Purpose = "Compound learning into better decisions and stronger communication.",
                CreatedAtUtc = now.AddDays(-95),
                UpdatedAtUtc = now.AddDays(-5)
            }
        ];
    }

    private static List<Resource> BuildResources(string userId, IReadOnlyList<Area> areas, DateTime now)
    {
        return
        [
            new Resource
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "AI Product Patterns",
                Description = "Reference material for designing trustworthy AI features and evaluation loops.",
                Area = areas[0],
                CreatedAtUtc = now.AddDays(-75),
                UpdatedAtUtc = now.AddDays(-4)
            },
            new Resource
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Research Methods",
                Description = "Interview scripts, survey design notes, synthesis methods, and discovery examples.",
                Area = areas[0],
                CreatedAtUtc = now.AddDays(-130),
                UpdatedAtUtc = now.AddDays(-9)
            },
            new Resource
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Writing and Storytelling",
                Description = "Reusable frameworks for briefs, launch narratives, memos, and public writing.",
                Area = areas[2],
                CreatedAtUtc = now.AddDays(-45),
                UpdatedAtUtc = now.AddDays(-2)
            },
            new Resource
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Quarterly Review Archive",
                Description = "Past review notes and retrospectives kept for future planning.",
                IsArchived = true,
                CreatedAtUtc = now.AddDays(-300),
                UpdatedAtUtc = now.AddDays(-60)
            }
        ];
    }

    private static List<Project> BuildProjects(string userId, IReadOnlyList<Area> areas, Faker faker, DateTime now)
    {
        var projectTemplates = new[]
        {
            new { Name = "Launch Today Dashboard", Outcome = "Ship a focused execution screen that surfaces current work without showing archived tasks.", Priority = ProjectPriority.Critical, Status = ProjectStatus.Active, DueInDays = 12 },
            new { Name = "Distillation Workflow Beta", Outcome = "Validate highlights, summaries, and action extraction with ten pilot users.", Priority = ProjectPriority.High, Status = ProjectStatus.Active, DueInDays = 28 },
            new { Name = "PARA Import Toolkit", Outcome = "Import notes from common tools and suggest initial PARA placement.", Priority = ProjectPriority.Medium, Status = ProjectStatus.Waiting, DueInDays = 45 },
            new { Name = "Q2 Knowledge Audit", Outcome = "Clean up stale resources and archive inactive projects from the previous quarter.", Priority = ProjectPriority.Low, Status = ProjectStatus.Completed, DueInDays = -7 }
        };

        return projectTemplates.Select((template, index) =>
        {
            var completedDate = template.Status == ProjectStatus.Completed ? now.AddDays(-4) : (DateTime?)null;

            return new Project
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = template.Name,
                Description = faker.Company.CatchPhrase(),
                DesiredOutcome = template.Outcome,
                Priority = template.Priority,
                Status = template.Status,
                StartDate = now.AddDays(-faker.Random.Int(45, 120)),
                DueDate = now.AddDays(template.DueInDays),
                CompletedDate = completedDate,
                Area = areas[index % areas.Count],
                CreatedAtUtc = now.AddDays(-faker.Random.Int(60, 180)),
                UpdatedAtUtc = completedDate ?? now.AddDays(-faker.Random.Int(1, 10))
            };
        }).ToList();
    }

    private static List<TaskItem> BuildTasks(string userId, IReadOnlyList<Project> projects, Faker faker, DateTime now)
    {
        var tasks = new List<TaskItem>();

        foreach (var project in projects)
        {
            var taskCount = project.Status == ProjectStatus.Completed ? 4 : 6;
            for (var index = 0; index < taskCount; index++)
            {
                var isDone = project.Status == ProjectStatus.Completed || index == taskCount - 1;
                var dueDate = index switch
                {
                    0 => now.AddDays(-2),
                    1 => now.Date,
                    2 => now.AddDays(3),
                    _ => now.AddDays(faker.Random.Int(7, 35))
                };

                tasks.Add(new TaskItem
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = faker.PickRandom(
                        "Draft decision memo",
                        "Review pilot feedback",
                        "Prepare stakeholder update",
                        "Refine success metrics",
                        "Schedule synthesis session",
                        "Turn notes into next actions",
                        "Validate edge cases with support"),
                    Description = faker.Lorem.Sentence(14),
                    Status = isDone ? TaskItemStatus.Done : faker.PickRandom(TaskItemStatus.Todo, TaskItemStatus.InProgress, TaskItemStatus.Waiting),
                    Priority = project.Priority == ProjectPriority.Critical ? TaskPriority.Critical : faker.PickRandom(TaskPriority.Low, TaskPriority.Medium, TaskPriority.High),
                    DueDate = dueDate,
                    CompletedDate = isDone ? now.AddDays(-faker.Random.Int(1, 8)) : null,
                    Project = project,
                    IsCurrentTask = project.Priority == ProjectPriority.Critical && index == 1,
                    CreatedAtUtc = now.AddDays(-faker.Random.Int(7, 70)),
                    UpdatedAtUtc = now.AddDays(-faker.Random.Int(0, 6))
                });
            }
        }

        var parent = tasks.First(task => task.IsCurrentTask);
        tasks.AddRange(
        [
            new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = "Confirm empty-state copy",
                Description = "Make sure the dashboard helps users decide what to do next.",
                Status = TaskItemStatus.Todo,
                Priority = TaskPriority.Medium,
                DueDate = now.AddDays(1),
                Project = parent.Project,
                ParentTask = parent,
                CreatedAtUtc = now.AddDays(-3),
                UpdatedAtUtc = now.AddDays(-1)
            },
            new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = "Check archived project filtering",
                Description = "Verify archived context stays out of Today and active project lists.",
                Status = TaskItemStatus.InProgress,
                Priority = TaskPriority.High,
                DueDate = now.Date,
                Project = parent.Project,
                ParentTask = parent,
                CreatedAtUtc = now.AddDays(-4),
                UpdatedAtUtc = now
            }
        ]);

        return tasks;
    }

    private static List<Tag> BuildTags(string userId, DateTime now)
    {
        var tags = new[]
        {
            ("strategy", "#2563EB"),
            ("customer-insight", "#059669"),
            ("meeting", "#D97706"),
            ("ai-generated", "#7C3AED"),
            ("writing", "#DB2777"),
            ("follow-up", "#DC2626")
        };

        return tags.Select(tag => new Tag
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = tag.Item1,
            Color = tag.Item2,
            CreatedAtUtc = now.AddDays(-40),
            UpdatedAtUtc = now.AddDays(-2)
        }).ToList();
    }

    private static List<Source> BuildSources(string userId, Faker faker, DateTime now)
    {
        return Enumerable.Range(0, 12).Select(index =>
        {
            var type = faker.PickRandom(SourceType.Url, SourceType.MeetingNotes, SourceType.Document, SourceType.Email, SourceType.Pdf);

            return new Source
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = type,
                Title = type == SourceType.MeetingNotes ? $"{faker.Company.Bs()} sync" : faker.Company.CatchPhrase(),
                Url = type == SourceType.Url ? faker.Internet.Url() : null,
                Author = faker.Name.FullName(),
                Reference = faker.Lorem.Sentence(9),
                CapturedAtUtc = now.AddDays(-faker.Random.Int(1, 90)),
                CreatedAtUtc = now.AddDays(-faker.Random.Int(1, 90)),
                UpdatedAtUtc = now.AddDays(-faker.Random.Int(0, 12))
            };
        }).ToList();
    }

    private static List<Note> BuildNotes(
        string userId,
        IReadOnlyList<Project> projects,
        IReadOnlyList<Area> areas,
        IReadOnlyList<Resource> resources,
        IReadOnlyList<Source> sources,
        IReadOnlyList<Tag> tags,
        Faker faker,
        DateTime now)
    {
        var notes = new List<Note>();
        var categories = new[] { ParaCategory.Project, ParaCategory.Project, ParaCategory.Area, ParaCategory.Resource, ParaCategory.Archive };

        for (var index = 0; index < 24; index++)
        {
            var category = categories[index % categories.Length];
            var content = string.Join(Environment.NewLine + Environment.NewLine, faker.Lorem.Paragraphs(faker.Random.Int(2, 4)));
            var status = category == ParaCategory.Archive
                ? NoteStatus.Archived
                : faker.PickRandom(NoteStatus.Inbox, NoteStatus.Active, NoteStatus.Distilled);

            var note = new Note
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = faker.PickRandom(
                    "Customer interview synthesis",
                    "Launch risks and mitigations",
                    "Dashboard prioritization notes",
                    "Progressive summarization examples",
                    "Research readout highlights",
                    "Weekly planning reflection",
                    "AI assistant trust principles") + $" #{index + 1}",
                Content = content,
                AiSummary = status == NoteStatus.Distilled ? faker.Lorem.Sentence(18) : null,
                Status = status,
                ParaCategory = category,
                Source = sources[index % sources.Count],
                Project = category == ParaCategory.Project ? projects[index % projects.Count] : null,
                Area = category == ParaCategory.Area ? areas[index % areas.Count] : null,
                Resource = category is ParaCategory.Resource or ParaCategory.Archive ? resources[index % resources.Count] : null,
                CreatedAtUtc = now.AddDays(-faker.Random.Int(1, 120)),
                UpdatedAtUtc = now.AddDays(-faker.Random.Int(0, 14))
            };

            foreach (var tag in faker.PickRandom(tags, faker.Random.Int(1, 3)))
            {
                note.Tags.Add(tag);
            }

            note.Highlights.Add(new Highlight
            {
                Id = Guid.NewGuid(),
                Text = faker.Lorem.Sentence(16),
                Annotation = faker.Lorem.Sentence(10),
                Layer = faker.Random.Int(1, 2),
                CreatedAtUtc = note.CreatedAtUtc.AddDays(1),
                UpdatedAtUtc = note.UpdatedAtUtc
            });

            if (status == NoteStatus.Distilled)
            {
                note.Summaries.Add(new Summary
                {
                    Id = Guid.NewGuid(),
                    Content = faker.Lorem.Paragraph(),
                    IsAiGenerated = true,
                    Model = "dev-seed-model",
                    PromptVersion = "summary-v1",
                    CreatedAtUtc = note.CreatedAtUtc.AddDays(2),
                    UpdatedAtUtc = note.UpdatedAtUtc
                });
            }

            if (index % 3 == 0)
            {
                note.ActionItems.Add(new ActionItem
                {
                    Id = Guid.NewGuid(),
                    UserId = note.UserId,
                    Title = faker.PickRandom("Follow up with pilot user", "Clarify owner", "Convert insight into task", "Add to launch memo"),
                    Description = faker.Lorem.Sentence(12),
                    Status = faker.PickRandom(ActionItemStatus.Open, ActionItemStatus.Done),
                    IsAiGenerated = true,
                    CreatedAtUtc = note.CreatedAtUtc.AddDays(1),
                    UpdatedAtUtc = note.UpdatedAtUtc
                });
            }

            notes.Add(note);
        }

        return notes;
    }

    private static List<Output> BuildOutputs(string userId, IReadOnlyList<Project> projects, IReadOnlyList<Note> notes, Faker faker, DateTime now)
    {
        return Enumerable.Range(0, 5).Select(index =>
        {
            var output = new Output
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = faker.PickRandom(
                    "Launch Brief",
                    "Pilot Research Summary",
                    "Decision Record: Today Scope",
                    "Knowledge Audit Report",
                    "LinkedIn Draft on Second Brain Habits"),
                Content = string.Join(Environment.NewLine + Environment.NewLine, faker.Lorem.Paragraphs(3)),
                Type = faker.PickRandom(OutputType.MeetingBrief, OutputType.Report, OutputType.DecisionRecord, OutputType.LinkedInPost, OutputType.ResearchSummary),
                Status = faker.PickRandom(OutputStatus.Draft, OutputStatus.InReview, OutputStatus.Published),
                IsAiGenerated = index % 2 == 0,
                Model = index % 2 == 0 ? "dev-seed-model" : null,
                PromptVersion = index % 2 == 0 ? "express-v1" : null,
                Project = projects[index % projects.Count],
                CreatedAtUtc = now.AddDays(-faker.Random.Int(1, 30)),
                UpdatedAtUtc = now.AddDays(-faker.Random.Int(0, 5))
            };

            foreach (var note in notes.Skip(index * 2).Take(4))
            {
                output.SourceNotes.Add(note);
            }

            return output;
        }).ToList();
    }

    private static List<NoteRelationship> BuildRelationships(IReadOnlyList<Note> notes, DateTime now)
    {
        return Enumerable.Range(0, 8).Select(index => new NoteRelationship
        {
            Id = Guid.NewGuid(),
            SourceNote = notes[index],
            TargetNote = notes[index + 1],
            Type = index % 2 == 0 ? RelationshipType.Supports : RelationshipType.Related,
            Annotation = "Seeded relationship showing how captured ideas connect across PARA contexts.",
            IsAiGenerated = index % 2 == 0,
            CreatedAtUtc = now.AddDays(-index - 1),
            UpdatedAtUtc = now.AddDays(-index)
        }).ToList();
    }
}
