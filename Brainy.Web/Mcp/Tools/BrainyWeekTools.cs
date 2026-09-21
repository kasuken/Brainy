using System.ComponentModel;
using Brainy.Application.DTOs.Week;
using Brainy.Application.Interfaces.Services;
using ModelContextProtocol.Server;

namespace Brainy.Web.Mcp.Tools;

/// <summary>
/// MCP tools for deliberate Monday–Sunday weekly planning, over <see cref="IWeekService"/>.
/// Adding or removing a task to/from the week only changes what is planned for the week — it
/// never mutates the task's status, due date, priority, or current-focus flag. Scoped per
/// request to the token's owner.
/// </summary>
[McpServerToolType]
internal sealed class BrainyWeekTools
{
    [McpServerTool(Name = "get_week")]
    [Description("Get the user's current-week plan: the week's dates, selection counts, the tasks " +
                 "selected for this week, and unfinished tasks from last week that could be carried forward.")]
    public static async Task<McpWeek> GetWeekAsync(
        IWeekService weekService,
        CancellationToken cancellationToken = default)
    {
        var overview = await weekService.GetCurrentWeekOverviewAsync(cancellationToken).ConfigureAwait(false);

        var selected = overview.SelectedTaskGroups
            .SelectMany(group => group.Tasks.Select(task => McpWeekTask.From(task)))
            .ToList();

        var carryForward = overview.CarryForwardCandidates
            .Where(candidate => candidate.CanCarryForward && !candidate.AlreadySelectedThisWeek)
            .Select(candidate => McpWeekTask.From(candidate.Task))
            .ToList();

        return new McpWeek(
            overview.WeekStartDate,
            overview.WeekEndDate,
            overview.WeekNumber,
            overview.SelectedTaskCount,
            overview.CompletedSelectedTaskCount,
            overview.RemainingSelectedTaskCount,
            selected,
            carryForward);
    }

    [McpServerTool(Name = "add_task_to_week")]
    [Description("Add a task to the current week's plan. Does not change the task's status, due date, " +
                 "priority, or current-focus flag.")]
    public static async Task<string> AddTaskToWeekAsync(
        IWeekService weekService,
        [Description("The id of the task to add to this week.")] Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await weekService.AddTaskToCurrentWeekAsync(taskId, cancellationToken).ConfigureAwait(false);
        return $"Task {taskId} added to the current week.";
    }

    [McpServerTool(Name = "remove_task_from_week")]
    [Description("Remove a task from the current week's plan. Does not change the task itself.")]
    public static async Task<string> RemoveTaskFromWeekAsync(
        IWeekService weekService,
        [Description("The id of the task to remove from this week.")] Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await weekService.RemoveTaskFromCurrentWeekAsync(taskId, cancellationToken).ConfigureAwait(false);
        return $"Task {taskId} removed from the current week.";
    }

    [McpServerTool(Name = "carry_forward_tasks")]
    [Description("Carry the given unfinished tasks from last week into the current week's plan. " +
                 "Use the carryForward list from get_week to choose candidate task ids.")]
    public static async Task<string> CarryForwardTasksAsync(
        IWeekService weekService,
        [Description("The ids of the previous-week tasks to carry into this week.")] IReadOnlyList<Guid> taskIds,
        CancellationToken cancellationToken = default)
    {
        if (taskIds is null || taskIds.Count == 0)
            return "No tasks were supplied to carry forward.";

        await weekService.CarryForwardTasksAsync(taskIds, cancellationToken).ConfigureAwait(false);
        return $"Carried {taskIds.Count} task(s) forward into the current week.";
    }
}

/// <summary>Compact current-week plan for MCP callers.</summary>
public record McpWeek(
    DateTime WeekStartDate,
    DateTime WeekEndDate,
    int WeekNumber,
    int SelectedTaskCount,
    int CompletedSelectedTaskCount,
    int RemainingSelectedTaskCount,
    [property: Description("Tasks currently selected for this week.")]
    IReadOnlyList<McpWeekTask> SelectedTasks,
    [property: Description("Unfinished tasks from last week eligible to carry forward.")]
    IReadOnlyList<McpWeekTask> CarryForwardCandidates);

/// <summary>Compact task projection used within a week plan.</summary>
public record McpWeekTask(
    Guid Id,
    string Title,
    Guid ProjectId,
    string ProjectName,
    string Status,
    DateTime? DueDate)
{
    public static McpWeekTask From(WeekTaskCardDto t) => new(
        t.Id,
        t.Title,
        t.ProjectId,
        t.ProjectName,
        t.Status.ToString(),
        t.DueDate);
}
