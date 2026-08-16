using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Brainy.Application.AI.Prompts;
using Brainy.Application.DTOs.Llm;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

internal sealed class LlmFocusExportService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IUserTimeZoneService userTimeZone,
    TimeProvider timeProvider) : ILlmFocusExportService
{
    private const string JsonContentType = "application/json;charset=utf-8";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Default,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<LlmFocusExportFileDto> ExportCurrentUserAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var timeZoneId = await userTimeZone.GetTimeZoneIdAsync(cancellationToken).ConfigureAwait(false);
        var generatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var projects = await context.Projects.AsNoTracking()
            .Where(project =>
                project.UserId == userId &&
                !project.IsArchived &&
                project.Status != ProjectStatus.Completed &&
                project.Status != ProjectStatus.Archived)
            .OrderByDescending(project => project.Priority)
            .ThenBy(project => project.DueDate)
            .ThenBy(project => project.Name)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.Description,
                project.DesiredOutcome,
                project.Status,
                project.Priority,
                project.StartDate,
                project.DueDate,
                Area = project.Area == null ? null : project.Area.Name,
                Goal = project.Goal == null ? null : project.Goal.Title,
                OpenTaskCount = project.Tasks.Count(task =>
                    task.UserId == userId &&
                    !task.IsArchived &&
                    task.Status != TaskItemStatus.Done &&
                    task.Status != TaskItemStatus.Archived),
                CompletedTaskCount = project.Tasks.Count(task =>
                    task.UserId == userId && task.Status == TaskItemStatus.Done)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var activeProjectIds = projects.Select(project => project.Id).ToList();
        var tasks = await context.Tasks.AsNoTracking()
            .Where(task =>
                task.UserId == userId &&
                activeProjectIds.Contains(task.ProjectId) &&
                !task.IsArchived &&
                task.Status != TaskItemStatus.Done &&
                task.Status != TaskItemStatus.Archived)
            .OrderByDescending(task => task.IsCurrentTask)
            .ThenBy(task => task.DueDate)
            .ThenByDescending(task => task.Priority)
            .ThenBy(task => task.Title)
            .Select(task => new
            {
                task.Id,
                task.ProjectId,
                Project = task.Project.Name,
                task.ParentTaskId,
                task.Title,
                task.Description,
                task.Status,
                task.Priority,
                task.DueDate,
                task.Complexity,
                task.IsCurrentTask,
                task.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var taskIds = tasks.Select(task => task.Id).ToList();
        var dependencies = await context.TaskDependencies.AsNoTracking()
            .Where(dependency =>
                dependency.Task.UserId == userId &&
                dependency.DependsOnTask.UserId == userId &&
                taskIds.Contains(dependency.TaskId) &&
                taskIds.Contains(dependency.DependsOnTaskId))
            .OrderBy(dependency => dependency.TaskId)
            .ThenBy(dependency => dependency.DependsOnTaskId)
            .Select(dependency => new
            {
                dependency.TaskId,
                dependency.DependsOnTaskId
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var goals = await context.Goals.AsNoTracking()
            .Where(goal =>
                goal.UserId == userId &&
                !goal.IsArchived &&
                (goal.Status == GoalStatus.Planned || goal.Status == GoalStatus.Active))
            .OrderBy(goal => goal.TargetDate)
            .ThenBy(goal => goal.Title)
            .Select(goal => new
            {
                goal.Id,
                goal.Title,
                goal.Description,
                goal.Status,
                goal.TargetDate,
                Area = goal.Area == null ? null : goal.Area.Name
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var inboxCount = await context.Notes.AsNoTracking()
            .CountAsync(note =>
                note.UserId == userId &&
                !note.IsArchived &&
                note.Status == NoteStatus.Inbox,
                cancellationToken)
            .ConfigureAwait(false);

        var snapshot = new
        {
            SchemaVersion = ILlmFocusExportService.SchemaVersion,
            Product = "Brainy",
            Purpose = "focus-planning",
            GeneratedAtUtc = generatedAtUtc,
            CalendarDate = today.ToString("yyyy-MM-dd"),
            TimeZoneId = timeZoneId,
            Privacy = new
            {
                SentAutomatically = false,
                Note = "Project, task, and goal text is exported as stored. Review it before sharing with an external LLM."
            },
            Prompt = new
            {
                Version = AiPrompts.FocusPlanningVersion,
                Text = AiPrompts.FocusPlanning
            },
            StatusSummary = new
            {
                ActiveProjectCount = projects.Count,
                OpenTaskCount = tasks.Count,
                OverdueTaskCount = tasks.Count(task => task.DueDate?.Date < today.Date),
                WaitingTaskCount = tasks.Count(task => task.Status == TaskItemStatus.Waiting),
                CurrentTaskCount = tasks.Count(task => task.IsCurrentTask),
                InboxCount = inboxCount
            },
            Data = new
            {
                Projects = projects,
                Tasks = tasks,
                TaskDependencies = dependencies,
                Goals = goals
            }
        };

        var content = JsonSerializer.SerializeToUtf8Bytes(snapshot, SerializerOptions);
        var fileName = $"brainy-focus-{today:yyyyMMdd}-v{ILlmFocusExportService.SchemaVersion}.json";

        return new LlmFocusExportFileDto(
            fileName,
            JsonContentType,
            ILlmFocusExportService.SchemaVersion,
            AiPrompts.FocusPlanningVersion,
            content);
    }
}
