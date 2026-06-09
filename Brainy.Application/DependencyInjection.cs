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
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IAreaService, AreaService>();
        services.AddScoped<IResourceService, ResourceService>();
        services.AddScoped<IParaSummaryService, ParaSummaryService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<INoteRelationshipService, NoteRelationshipService>();
        return services;
    }
}
