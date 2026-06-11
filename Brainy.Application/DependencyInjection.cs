using Brainy.Application.Interfaces.Services;
using Brainy.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Brainy.Application;

/// <summary>
/// Extension methods for registering Brainy application services with the DI container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers all application-layer services (note management, project management, etc.).
    /// </summary>
    public static IServiceCollection AddBrainyApplication(this IServiceCollection services)
    {
        services.AddScoped<INoteService, NoteService>();
        services.AddScoped<INoteImageService, NoteImageService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IAreaService, AreaService>();
        services.AddScoped<IResourceService, ResourceService>();
        services.AddScoped<IParaSummaryService, ParaSummaryService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<INoteRelationshipService, NoteRelationshipService>();
        services.AddScoped<IRelatedNotesService, RelatedNotesService>();
        services.AddScoped<ITodayService, TodayService>();
        services.AddScoped<IProjectPrioritizationService, ProjectPrioritizationService>();
        services.AddScoped<ICurrentTaskRecommendationService, CurrentTaskRecommendationService>();
        services.AddScoped<ITodayNotificationService, TodayNotificationService>();
        services.AddScoped<IUserDashboardPreferenceService, UserDashboardPreferenceService>();
        services.AddScoped<IArchiveRetentionService, ArchiveRetentionService>();
        services.AddScoped<IInboxMetricsService, InboxMetricsService>();
        services.AddScoped<IInboxSuggestionsService, InboxSuggestionsService>();
        services.AddScoped<IIdeaService, IdeaService>();
        services.AddScoped<ITasksHubService, TasksHubService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<IGoalService, GoalService>();
        services.AddScoped<IGoalMilestoneService, GoalMilestoneService>();
        return services;
    }
}
