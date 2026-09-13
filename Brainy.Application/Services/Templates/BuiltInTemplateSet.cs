using Brainy.Domain.Entities;
using Brainy.Domain.Enums;

namespace Brainy.Application.Services.Templates;

/// <summary>
/// The small starter set of built-in project, note and output templates seeded for a
/// user the first time they have none of that type, so the feature is useful without
/// any setup (see AGENTS.md — "day one" usefulness). Built-in templates have no
/// default area, since a brand-new user has none yet; instantiating one simply asks
/// for an area the same way creating a project by hand would.
/// </summary>
internal static class BuiltInTemplateSet
{
    public static IReadOnlyList<ProjectTemplate> BuildProjectTemplates(string userId)
    {
        var templates = new List<ProjectTemplate>
        {
            new()
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Client Engagement Kickoff",
                ProjectNamePattern = "Client Engagement — {Date}",
                Description = "Standard kickoff for a new consulting or freelance engagement.",
                DesiredOutcome = "Scope is agreed, kickoff is held, and the client has a working cadence with you.",
                DefaultPriority = ProjectPriority.High,
                IsBuiltIn = true,
                Tasks = Tasks(
                    ("Send kickoff agenda", 0, TaskPriority.High),
                    ("Hold kickoff call", 2, TaskPriority.High),
                    ("Confirm scope and success criteria in writing", 3, TaskPriority.Medium),
                    ("Set up shared workspace", 4, TaskPriority.Medium),
                    ("Schedule first check-in", 7, TaskPriority.Medium))
            },
            new()
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Weekly Status Report",
                ProjectNamePattern = "Weekly Status Report — {Date}",
                Description = "Recurring cadence for pulling a status update together and sending it out.",
                DefaultPriority = ProjectPriority.Medium,
                IsBuiltIn = true,
                Tasks = Tasks(
                    ("Gather updates from the team", 0, TaskPriority.Medium),
                    ("Draft the report", 1, TaskPriority.Medium),
                    ("Send the report", 2, TaskPriority.Medium))
            }
        };

        return templates;
    }

    public static IReadOnlyList<NoteTemplate> BuildNoteTemplates(string userId) =>
    [
        new NoteTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Meeting Notes",
            TitlePattern = "Meeting Notes — {Date}",
            ContentScaffold = "## Attendees\n\n## Agenda\n\n## Discussion\n\n## Decisions\n\n## Action Items\n",
            DefaultParaCategory = ParaCategory.Project,
            IsBuiltIn = true
        },
        new NoteTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Weekly Review",
            TitlePattern = "Weekly Review — {Date}",
            ContentScaffold = "## Wins this week\n\n## Challenges\n\n## Next week's focus\n",
            DefaultParaCategory = ParaCategory.Area,
            IsBuiltIn = true
        }
    ];

    public static IReadOnlyList<OutputTemplate> BuildOutputTemplates(string userId) =>
    [
        new OutputTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Blog Post Draft",
            TitlePattern = "Blog Post — {Date}",
            Type = OutputType.BlogPost,
            ContentScaffold = "## Hook\n\n## Key Points\n\n## Call to Action\n",
            DefaultSourceSelection = OutputTemplateSourceSelectionMode.ActiveProjectNotes,
            IsBuiltIn = true
        },
        new OutputTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Meeting Brief",
            TitlePattern = "Meeting Brief — {Date}",
            Type = OutputType.MeetingBrief,
            ContentScaffold = "## Purpose\n\n## Background\n\n## Discussion Points\n\n## Desired Outcome\n",
            DefaultSourceSelection = OutputTemplateSourceSelectionMode.ActiveProjectNotes,
            IsBuiltIn = true
        }
    ];

    private static List<ProjectTemplateTask> Tasks(params (string Title, int DueDateOffsetDays, TaskPriority Priority)[] tasks) =>
        tasks.Select((t, index) => new ProjectTemplateTask
        {
            Id = Guid.NewGuid(),
            Title = t.Title,
            Priority = t.Priority,
            DueDateOffsetDays = t.DueDateOffsetDays,
            SortOrder = index
        }).ToList();
}
