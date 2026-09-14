using Azure;
using Azure.AI.OpenAI;
using Brainy.Application.AI;
using Brainy.Application.Billing;
using Brainy.Application.Caching;
using Brainy.Application.Email;
using Brainy.Application.Interfaces.AI;
using Brainy.Application.Interfaces.Billing;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Email;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Options;
using Brainy.Application.Push;
using Brainy.Application.Services;
using Brainy.Application.Services.ExternalImport;
using Brainy.Application.Services.MarkdownExport;
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
        services.TryAddSingleton<IApplicationCache, MemoryApplicationCache>();
        // Overridden by Brainy.Web with a real Identity-backed implementation; see that
        // interface's remarks. Kept as a safe zero-count default for hosts/tests that don't.
        services.TryAddScoped<Brainy.Application.Interfaces.Identity.IUserDirectoryService, NullUserDirectoryService>();

        services.AddScoped<INoteService, NoteService>();
        services.AddScoped<INoteRevisionService, NoteRevisionService>();
        services.AddScoped<IShareCaptureService, ShareCaptureService>();
        // Kept as a deliberate no-op: issue #302 ("Offline Lite") ended up implementing real
        // offline persistence entirely client-side (IndexedDB + a plain sync endpoint, see
        // offlineCapture.js and IOfflineCaptureSyncService) because a fully offline browser
        // has no live circuit to call this seam from at all. See NullOfflineCaptureQueue for
        // the "circuit-alive-but-flaky" case this was reserved for and why it is unneeded.
        services.TryAddScoped<IOfflineCaptureQueue, NullOfflineCaptureQueue>();
        services.AddScoped<IOfflineSnapshotService, OfflineSnapshotService>();
        services.AddScoped<IOfflineCaptureSyncService, OfflineCaptureSyncService>();
        services.AddScoped<INoteImageService, NoteImageService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IAreaService, AreaService>();
        services.AddScoped<IResourceService, ResourceService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<IParaSummaryService, ParaSummaryService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<INoteRelationshipService, NoteRelationshipService>();
        services.AddScoped<IRelatedNotesService, RelatedNotesService>();
        services.AddScoped<ITodayService, TodayService>();
        services.AddScoped<IWeekService, WeekService>();
        services.AddScoped<IWeeklyReviewService, WeeklyReviewService>();
        services.AddScoped<IProjectPrioritizationService, ProjectPrioritizationService>();
        services.AddScoped<ICurrentTaskRecommendationService, CurrentTaskRecommendationService>();
        services.AddScoped<IResumeContextService, ResumeContextService>();
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
        services.AddScoped<ICalendarFeedService, CalendarFeedService>();
        services.AddScoped<ICalendarFeedTokenService, CalendarFeedTokenService>();
        services.AddScoped<IGoalService, GoalService>();
        services.AddScoped<IGoalMilestoneService, GoalMilestoneService>();
        services.AddScoped<IOutputService, OutputService>();
        services.AddScoped<IOutputShareLinkService, OutputShareLinkService>();
        services.AddScoped<IHighlightService, HighlightService>();
        services.AddScoped<ISummaryService, SummaryService>();
        services.AddScoped<IActionItemService, ActionItemService>();
        services.AddScoped<IPulseService, PulseService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IDataExportService, DataExportService>();
        services.AddScoped<IDataImportService, DataImportService>();
        services.AddScoped<IExternalImportService, ExternalImportService>();
        services.AddScoped<IMarkdownExportService, MarkdownExportService>();
        // Single shared instance so the background consumer (Web's hosted service) and every
        // request-scoped IMarkdownExportJobService see the same in-memory queue and job store.
        services.TryAddSingleton<InMemoryMarkdownExportJobCoordinator>();
        services.TryAddSingleton<IMarkdownExportJobQueue>(
            provider => provider.GetRequiredService<InMemoryMarkdownExportJobCoordinator>());
        services.TryAddSingleton<IMarkdownExportJobStore>(
            provider => provider.GetRequiredService<InMemoryMarkdownExportJobCoordinator>());
        services.AddScoped<IMarkdownExportJobService, MarkdownExportJobService>();
        services.AddScoped<ILlmFocusExportService, LlmFocusExportService>();
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<IBillingWebhookProcessor, BillingWebhookProcessor>();
        services.AddScoped<IProjectTemplateService, ProjectTemplateService>();
        services.AddScoped<INoteTemplateService, NoteTemplateService>();
        services.AddScoped<IOutputTemplateService, OutputTemplateService>();
        services.AddScoped<IPushSubscriptionService, PushSubscriptionService>();
        services.AddScoped<IPushNotificationPreferenceService, PushNotificationPreferenceService>();
        services.AddScoped<IPushDispatchService, PushDispatchService>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="IPushNotificationSender"/> based on the <c>WebPush</c>
    /// configuration section. When the VAPID key pair is not fully configured (the default),
    /// <see cref="NullPushNotificationSender"/> is registered so the app starts and every
    /// push attempt is logged instead of sent — mirroring <see cref="AddEmail"/>.
    /// </summary>
    public static IServiceCollection AddWebPush(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WebPushOptions>(configuration.GetSection(WebPushOptions.SectionName));

        var options = configuration
            .GetSection(WebPushOptions.SectionName)
            .Get<WebPushOptions>() ?? new WebPushOptions();

        if (options.IsConfigured)
        {
            services.AddSingleton<IPushNotificationSender, WebPushNotificationSender>();
        }
        else
        {
            services.AddSingleton<IPushNotificationSender, NullPushNotificationSender>();
        }

        return services;
    }

    /// <summary>
    /// Registers <see cref="IBillingProvider"/> based on the <c>Billing</c> configuration
    /// section. When <see cref="BillingProviderType.None"/> is configured (the default),
    /// <see cref="NullBillingProvider"/> is registered so the entitlement system works fully
    /// without a live payment-provider account.
    /// </summary>
    public static IServiceCollection AddBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BillingOptions>(configuration.GetSection(BillingOptions.SectionName));

        var options = configuration
            .GetSection(BillingOptions.SectionName)
            .Get<BillingOptions>() ?? new BillingOptions();

        switch (options.Provider)
        {
            case BillingProviderType.None:
                services.AddSingleton<IBillingProvider, NullBillingProvider>();
                break;

            case BillingProviderType.Stripe:
                ArgumentException.ThrowIfNullOrWhiteSpace(options.ApiKey, nameof(options.ApiKey));
                ArgumentException.ThrowIfNullOrWhiteSpace(options.WebhookSigningSecret, nameof(options.WebhookSigningSecret));
                ArgumentException.ThrowIfNullOrWhiteSpace(options.ProPriceId, nameof(options.ProPriceId));
                ArgumentException.ThrowIfNullOrWhiteSpace(options.AppBaseUrl, nameof(options.AppBaseUrl));

                // Scoped: StripeBillingProvider reads/writes the per-request IApplicationDbContext.
                services.AddScoped<IBillingProvider, StripeBillingProvider>();
                break;

            default:
                throw new InvalidOperationException($"Unsupported billing provider: {options.Provider}");
        }

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

    /// <summary>
    /// Registers <see cref="IEmailSender"/> based on the <c>Email</c> configuration section.
    /// When <see cref="EmailProviderType.None"/> is configured (the default),
    /// <see cref="NullEmailSender"/> is registered so the app starts and every outbound
    /// message is logged instead of sent — local development and self-hosting keep working
    /// without a mail account configured. Also registers <see cref="IAccountEmailService"/>,
    /// the templated seam the Web layer's Identity email adapter calls into.
    /// </summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        var options = configuration
            .GetSection(EmailOptions.SectionName)
            .Get<EmailOptions>() ?? new EmailOptions();

        switch (options.Provider)
        {
            case EmailProviderType.None:
                services.AddSingleton<IEmailSender, NullEmailSender>();
                break;

            case EmailProviderType.Smtp:
                ArgumentException.ThrowIfNullOrWhiteSpace(options.SmtpHost, nameof(options.SmtpHost));
                ArgumentException.ThrowIfNullOrWhiteSpace(options.FromAddress, nameof(options.FromAddress));

                services.AddSingleton(options);
                services.AddSingleton<IEmailSender, SmtpEmailSender>();
                break;

            default:
                throw new InvalidOperationException($"Unsupported email provider: {options.Provider}");
        }

        services.AddSingleton<IAccountEmailService, AccountEmailService>();
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
