using System.Data;
using Brainy.Data;
using Brainy.Data.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Web.Identity;

/// <summary>
/// The outcome of a current-user account deletion request.
/// </summary>
public enum AccountDeletionResult
{
    /// <summary>The account and its data were deleted.</summary>
    Succeeded,

    /// <summary>The authenticated Identity user no longer exists.</summary>
    InvalidSession,

    /// <summary>The supplied current password was not valid.</summary>
    InvalidPassword,

    /// <summary>The required destructive-action phrase was not supplied exactly.</summary>
    InvalidConfirmation,
}

/// <summary>
/// Deletes only the currently authenticated user's account and Brainy data.
/// </summary>
public interface IAccountDeletionService
{
    /// <summary>
    /// Verifies the current password and confirmation phrase, then deletes the
    /// authenticated user's account and owned data in one database transaction.
    /// </summary>
    /// <param name="currentPassword">The user's current password.</param>
    /// <param name="confirmation">The exact destructive-action confirmation phrase.</param>
    /// <param name="cancellationToken">Token used to cancel database work.</param>
    /// <returns>The validation or deletion outcome.</returns>
    Task<AccountDeletionResult> DeleteCurrentUserAsync(
        string currentPassword,
        string confirmation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Identity and EF Core orchestration for self-service account deletion.
/// </summary>
public sealed class AccountDeletionService(
    AuthenticationStateProvider authenticationStateProvider,
    UserManager<ApplicationUser> userManager,
    BrainyDbContext context) : IAccountDeletionService
{
    /// <summary>The exact phrase required before an account can be deleted.</summary>
    public const string ConfirmationPhrase = "DELETE MY ACCOUNT";

    /// <inheritdoc />
    public async Task<AccountDeletionResult> DeleteCurrentUserAsync(
        string currentPassword,
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(confirmation, ConfirmationPhrase, StringComparison.Ordinal))
            return AccountDeletionResult.InvalidConfirmation;

        if (string.IsNullOrEmpty(currentPassword))
            return AccountDeletionResult.InvalidPassword;

        var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var user = await userManager.GetUserAsync(authenticationState.User).ConfigureAwait(false);
        if (user is null)
            return AccountDeletionResult.InvalidSession;

        if (!await userManager.CheckPasswordAsync(user, currentPassword).ConfigureAwait(false))
            return AccountDeletionResult.InvalidPassword;

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);

            await EnsureNoCrossUserDependentsAsync(user.Id, cancellationToken).ConfigureAwait(false);
            await DeleteOwnedDataAsync(user.Id, cancellationToken).ConfigureAwait(false);

            var deletedUsers = await context.Users
                .Where(candidate => candidate.Id == user.Id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deletedUsers != 1)
                throw new InvalidOperationException("The authenticated account no longer exists.");

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        // UserManager loaded the account into this scoped Identity context. ExecuteDelete
        // intentionally bypasses tracking, so detach stale state before the final sign-out request.
        context.ChangeTracker.Clear();
        return AccountDeletionResult.Succeeded;
    }

    private async Task DeleteOwnedDataAsync(string userId, CancellationToken cancellationToken)
    {
        // Restrictive and self-referential relationships must be removed before their
        // principal rows. ExecuteDelete is intentional: lifecycle entries are normally
        // append-only, but account erasure must remove them without invoking audit hooks.
        await context.TaskDependencies
            .Where(dependency => dependency.Task.UserId == userId || dependency.DependsOnTask.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.NoteRelationships
            .Where(relationship => relationship.SourceNote.UserId == userId || relationship.TargetNote.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.ActionItems.Where(item => item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.GoalActivities.Where(activity => activity.Goal.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.GoalMilestones.Where(milestone => milestone.Goal!.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Highlights.Where(highlight => highlight.Note.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Summaries.Where(summary => summary.Note.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.NoteImages.Where(image => image.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.LifecycleActivities.Where(activity => activity.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        await context.Outputs.Where(output => output.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Ideas.Where(idea => idea.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Notes.Where(note => note.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Tasks.Where(task => task.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Projects.Where(project => project.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Goals.Where(goal => goal.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Resources.Where(resource => resource.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Sources.Where(source => source.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Tags.Where(tag => tag.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Areas.Where(area => area.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.ArchiveRetentionRules.Where(rule => rule.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.DashboardPreferences.Where(preference => preference.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureNoCrossUserDependentsAsync(string userId, CancellationToken cancellationToken)
    {
        // These relationships use Cascade or SetNull. A corrupt cross-tenant link
        // must abort deletion instead of deleting or modifying another user's row.
        var hasForeignTaskReference = await context.Tasks.AnyAsync(task =>
                task.UserId != userId &&
                (context.Projects.Any(project => project.Id == task.ProjectId && project.UserId == userId) ||
                 (task.ParentTaskId.HasValue && context.Tasks.Any(parent =>
                     parent.Id == task.ParentTaskId.Value && parent.UserId == userId))),
            cancellationToken).ConfigureAwait(false);

        var hasForeignProjectReference = await context.Projects.AnyAsync(project =>
                project.UserId != userId &&
                ((project.AreaId.HasValue && context.Areas.Any(area => area.Id == project.AreaId.Value && area.UserId == userId)) ||
                 (project.GoalId.HasValue && context.Goals.Any(goal => goal.Id == project.GoalId.Value && goal.UserId == userId))),
            cancellationToken).ConfigureAwait(false);

        var hasForeignResourceReference = await context.Resources.AnyAsync(resource =>
                resource.UserId != userId && resource.AreaId.HasValue &&
                context.Areas.Any(area => area.Id == resource.AreaId.Value && area.UserId == userId),
            cancellationToken).ConfigureAwait(false);

        var hasForeignGoalReference = await context.Goals.AnyAsync(goal =>
                goal.UserId != userId && goal.AreaId.HasValue &&
                context.Areas.Any(area => area.Id == goal.AreaId.Value && area.UserId == userId),
            cancellationToken).ConfigureAwait(false);

        var hasForeignIdeaReference = await context.Ideas.AnyAsync(idea =>
                idea.UserId != userId &&
                ((idea.AreaId.HasValue && context.Areas.Any(area => area.Id == idea.AreaId.Value && area.UserId == userId)) ||
                 (idea.CommittedProjectId.HasValue && context.Projects.Any(project =>
                     project.Id == idea.CommittedProjectId.Value && project.UserId == userId))),
            cancellationToken).ConfigureAwait(false);

        var hasForeignOutputReference = await context.Outputs.AnyAsync(output =>
                output.UserId != userId &&
                ((output.AreaId.HasValue && context.Areas.Any(area => area.Id == output.AreaId.Value && area.UserId == userId)) ||
                 (output.ProjectId.HasValue && context.Projects.Any(project => project.Id == output.ProjectId.Value && project.UserId == userId)) ||
                 (output.GoalId.HasValue && context.Goals.Any(goal => goal.Id == output.GoalId.Value && goal.UserId == userId))),
            cancellationToken).ConfigureAwait(false);

        var hasForeignNoteReference = await context.Notes.AnyAsync(note =>
                note.UserId != userId &&
                ((note.AreaId.HasValue && context.Areas.Any(area => area.Id == note.AreaId.Value && area.UserId == userId)) ||
                 (note.ProjectId.HasValue && context.Projects.Any(project => project.Id == note.ProjectId.Value && project.UserId == userId)) ||
                 (note.ResourceId.HasValue && context.Resources.Any(resource => resource.Id == note.ResourceId.Value && resource.UserId == userId)) ||
                 (note.SourceId.HasValue && context.Sources.Any(source => source.Id == note.SourceId.Value && source.UserId == userId))),
            cancellationToken).ConfigureAwait(false);

        var hasForeignImageReference = await context.NoteImages.AnyAsync(image =>
                image.UserId != userId && image.NoteId.HasValue &&
                context.Notes.Any(note => note.Id == image.NoteId.Value && note.UserId == userId),
            cancellationToken).ConfigureAwait(false);

        var hasForeignActionReference = await context.ActionItems.AnyAsync(item =>
                item.UserId != userId &&
                ((item.NoteId.HasValue && context.Notes.Any(note => note.Id == item.NoteId.Value && note.UserId == userId)) ||
                 (item.TaskItemId.HasValue && context.Tasks.Any(task => task.Id == item.TaskItemId.Value && task.UserId == userId))),
            cancellationToken).ConfigureAwait(false);

        var hasCrossUserTaskDependency = await context.TaskDependencies.AnyAsync(dependency =>
                (dependency.Task.UserId == userId && dependency.DependsOnTask.UserId != userId) ||
                (dependency.Task.UserId != userId && dependency.DependsOnTask.UserId == userId),
            cancellationToken).ConfigureAwait(false);

        var hasCrossUserNoteRelationship = await context.NoteRelationships.AnyAsync(relationship =>
                (relationship.SourceNote.UserId == userId && relationship.TargetNote.UserId != userId) ||
                (relationship.SourceNote.UserId != userId && relationship.TargetNote.UserId == userId),
            cancellationToken).ConfigureAwait(false);

        var hasCrossUserNoteTag = await context.Notes.AnyAsync(note =>
                (note.UserId == userId && note.Tags.Any(tag => tag.UserId != userId)) ||
                (note.UserId != userId && note.Tags.Any(tag => tag.UserId == userId)),
            cancellationToken).ConfigureAwait(false);

        var hasCrossUserResourceTag = await context.Resources.AnyAsync(resource =>
                (resource.UserId == userId && resource.Tags.Any(tag => tag.UserId != userId)) ||
                (resource.UserId != userId && resource.Tags.Any(tag => tag.UserId == userId)),
            cancellationToken).ConfigureAwait(false);

        var hasCrossUserOutputNote = await context.Outputs.AnyAsync(output =>
                (output.UserId == userId && output.SourceNotes.Any(note => note.UserId != userId)) ||
                (output.UserId != userId && output.SourceNotes.Any(note => note.UserId == userId)),
            cancellationToken).ConfigureAwait(false);

        if (hasForeignTaskReference || hasForeignProjectReference || hasForeignResourceReference ||
            hasForeignGoalReference || hasForeignIdeaReference || hasForeignOutputReference ||
            hasForeignNoteReference || hasForeignImageReference || hasForeignActionReference ||
            hasCrossUserTaskDependency || hasCrossUserNoteRelationship || hasCrossUserNoteTag ||
            hasCrossUserResourceTag || hasCrossUserOutputNote)
        {
            throw new InvalidOperationException(
                "Account deletion was stopped because inconsistent cross-account relationships were detected.");
        }
    }
}
