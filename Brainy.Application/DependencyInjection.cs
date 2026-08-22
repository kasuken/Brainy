using Azure;
using Azure.AI.OpenAI;
using Brainy.Application.AI;
using Brainy.Application.Interfaces.AI;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Options;
using Brainy.Application.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenAI.Chat;

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
        // Single source of "now"/"today" for due-date logic; tests and hosts may
        // register their own TimeProvider before calling this to override it.
        services.TryAddSingleton(TimeProvider.System);

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
        // Layouts and pages resolve this service concurrently during Blazor SSR.
        // Keep its captured transient DbContext isolated per consumer.
        services.AddTransient<IUserTimeZoneService, UserTimeZoneService>();
        services.AddScoped<IArchiveRetentionService, ArchiveRetentionService>();
        services.AddScoped<IInboxMetricsService, InboxMetricsService>();
        services.AddScoped<IInboxSuggestionsService, InboxSuggestionsService>();
        services.AddScoped<IIdeaService, IdeaService>();
        services.AddScoped<ITasksHubService, TasksHubService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<IGoalService, GoalService>();
        services.AddScoped<IGoalMilestoneService, GoalMilestoneService>();
        services.AddScoped<IOutputService, OutputService>();
        services.AddScoped<IHighlightService, HighlightService>();
        services.AddScoped<ISummaryService, SummaryService>();
        services.AddScoped<IActionItemService, ActionItemService>();
        services.AddScoped<IPulseService, PulseService>();
        services.AddScoped<IDataExportService, DataExportService>();
        services.AddScoped<IDataImportService, DataImportService>();
        services.AddScoped<ILlmFocusExportService, LlmFocusExportService>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="IAiAssistant"/> based on the <c>AiAssistant</c> configuration section.
    /// When <see cref="AiProviderType.None"/> is configured, a no-op implementation is registered
    /// so callers always receive a graceful response instead of an exception.
    /// </summary>
    public static IServiceCollection AddAiAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiAssistantOptions>(configuration.GetSection(AiAssistantOptions.SectionName));

        var options = configuration
            .GetSection(AiAssistantOptions.SectionName)
            .Get<AiAssistantOptions>() ?? new AiAssistantOptions();

        if (options.Provider == AiProviderType.None)
        {
            services.AddSingleton<IAiAssistant, NullAiAssistant>();
            return services;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApiKey, nameof(options.ApiKey));

        IChatClient chatClient = options.Provider switch
        {
            AiProviderType.OpenAI =>
                new ChatClient(options.Model, options.ApiKey).AsIChatClient(),

            AiProviderType.AzureOpenAI =>
                CreateAzureOpenAIChatClient(options),

            _ => throw new InvalidOperationException($"Unsupported AI provider: {options.Provider}"),
        };

        services.AddSingleton(chatClient);
        services.AddSingleton<IAiAssistant, OpenAiAssistant>();
        return services;
    }

    /// <summary>
    /// Registers a disabled AI assistant implementation regardless of configuration.
    /// Use this to temporarily turn off all AI-powered features without removing AI code.
    /// </summary>
    public static IServiceCollection AddDisabledAiAssistant(this IServiceCollection services)
    {
        services.AddSingleton<IAiAssistant, NullAiAssistant>();
        return services;
    }

    private static IChatClient CreateAzureOpenAIChatClient(AiAssistantOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Endpoint, nameof(options.Endpoint));

        var azureClient = new AzureOpenAIClient(
            new Uri(options.Endpoint),
            new AzureKeyCredential(options.ApiKey!));

        var deployment = options.DeploymentName ?? options.Model;
        return azureClient.GetChatClient(deployment).AsIChatClient();
    }
}
